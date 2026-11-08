using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

public sealed record CancellationOutcome(bool Succeeded, string Message);

/// <summary>
/// Rạp hủy vé khi KHÔNG chiếu được suất đó (hỏng máy, đóng chi nhánh...). Khách không tự hủy được: vé đã mua là
/// chốt, rạp chỉ hoàn tiền khi lỗi nằm ở phía rạp. Hủy xong ghế được trả lại, đơn ra khỏi doanh thu, lượt voucher
/// được trả lại và khách nhận email. Tiền do rạp hoàn thủ công rồi đánh dấu "Đã hoàn tiền" trong chi tiết đơn.
/// </summary>
public sealed class BookingCancellationService
{
    public const int MaxReasonLength = 300;

    private readonly CinemaDbContext _db;
    private readonly TimeProvider _clock;
    private readonly DisplayTimeZone _tz;
    private readonly SeatBroadcaster? _hub;
    private readonly ILogger<BookingCancellationService>? _log;

    public BookingCancellationService(
        CinemaDbContext db, TimeProvider clock, DisplayTimeZone tz,
        SeatBroadcaster? hub = null, ILogger<BookingCancellationService>? log = null)
    {
        _db = db;
        _clock = clock;
        _tz = tz;
        _hub = hub;
        _log = log;
    }

    /// <summary>Lý do chưa được hủy; null = hủy được.</summary>
    public static string? WhyNotCancellable(BookingStatus status, DateTime? checkedInUtc, DateTime startUtc, DateTime nowUtc)
    {
        if (status != BookingStatus.Confirmed) return "Chỉ hủy được đơn đã xác nhận.";
        if (checkedInUtc is not null) return "Vé đã được soát, không hủy được.";
        if (startUtc <= nowUtc) return "Suất đã bắt đầu, không hủy được.";
        return null;
    }

    public async Task<CancellationOutcome> CancelAsync(int bookingId, string? reason, string actor, CancellationToken ct = default)
    {
        var cleanReason = CleanReason(reason);
        if (cleanReason is null) return new(false, "Nhập lý do hủy để gửi cho khách.");

        var booking = await LoadAsync(_db.Bookings.Where(b => b.Id == bookingId), ct);
        var ticket = booking.SingleOrDefault();
        if (ticket is null) return new(false, "Không tìm thấy đơn.");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var blocked = WhyNotCancellable(ticket.Status, ticket.CheckedInUtc, ticket.Showtime!.StartTimeUtc, nowUtc);
        if (blocked is not null) return new(false, blocked);

        var freed = Apply(ticket, cleanReason, nowUtc);
        await _db.SaveChangesAsync(ct);
        await NotifySeatsAsync(ticket.ShowtimeId, freed, ct);

        _log?.LogInformation("{Actor} hủy vé {Code}: {Reason}", actor, ticket.BookingCode, cleanReason);
        return new(true, $"Đã hủy vé {ticket.BookingCode}, ghế đã được trả lại và khách sẽ nhận email. Nhớ hoàn {ticket.TotalAmount.Vnd()} cho khách.");
    }

    /// <summary>Hủy mọi vé hủy được của một suất rồi ẩn suất: dùng khi rạp không chiếu được cả suất.</summary>
    public async Task<CancellationOutcome> CancelShowtimeAsync(int showtimeId, string? reason, string actor, CancellationToken ct = default)
    {
        var cleanReason = CleanReason(reason);
        if (cleanReason is null) return new(false, "Nhập lý do hủy để gửi cho khách.");

        var showtime = await _db.Showtimes.FirstOrDefaultAsync(s => s.Id == showtimeId, ct);
        if (showtime is null) return new(false, "Không tìm thấy suất chiếu.");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        if (showtime.StartTimeUtc <= nowUtc) return new(false, "Suất đã bắt đầu, không hủy được.");

        // Khách đang ở cổng thanh toán vẫn có thể được xác nhận sau khi suất đã hủy: đợi đơn đó ngã ngũ.
        var paying = await _db.Bookings.CountAsync(b => b.ShowtimeId == showtimeId && b.Status == BookingStatus.Pending, ct);
        if (paying > 0)
            return new(false, $"Còn {paying} đơn đang thanh toán, vui lòng thử lại sau vài phút.");

        var tickets = await LoadAsync(_db.Bookings.Where(b => b.ShowtimeId == showtimeId && b.Status == BookingStatus.Confirmed), ct);
        var cancellable = tickets
            .Where(b => WhyNotCancellable(b.Status, b.CheckedInUtc, b.Showtime!.StartTimeUtc, nowUtc) is null)
            .ToList();

        var freed = cancellable.SelectMany(b => Apply(b, cleanReason, nowUtc)).ToList();
        showtime.IsActive = false;
        await _db.SaveChangesAsync(ct);
        await NotifySeatsAsync(showtimeId, freed, ct);

        var refund = cancellable.Sum(b => b.TotalAmount);
        _log?.LogInformation("{Actor} hủy suất {Showtime}, {Count} vé: {Reason}", actor, showtimeId, cancellable.Count, cleanReason);
        return new(true, $"Đã ẩn suất chiếu và hủy {cancellable.Count} đơn. Cần hoàn {refund.Vnd()} cho khách.");
    }

    public async Task<CancellationOutcome> MarkRefundedAsync(int bookingId, CancellationToken ct = default)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.BookingId == bookingId, ct);
        if (payment?.Status != PaymentStatus.RefundPending) return new(false, "Đơn này không ở trạng thái chờ hoàn tiền.");

        payment.Status = PaymentStatus.Refunded;
        await _db.SaveChangesAsync(ct);
        return new(true, "Đã ghi nhận hoàn tiền cho khách.");
    }

    private static string? CleanReason(string? reason)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= MaxReasonLength ? text : text[..MaxReasonLength];
    }

    private static Task<List<Booking>> LoadAsync(IQueryable<Booking> source, CancellationToken ct) =>
        source
            .Include(b => b.Payment)
            .Include(b => b.Seats).ThenInclude(s => s.Seat)
            .Include(b => b.Showtime!).ThenInclude(s => s.Movie)
            .Include(b => b.Showtime!).ThenInclude(s => s.Room!).ThenInclude(r => r.Theater)
            .ToListAsync(ct);

    /// <summary>Đổi trạng thái, trả ghế (unique index nằm trên BookingSeat nên phải xóa dòng) và xếp email.</summary>
    private List<int> Apply(Booking booking, string reason, DateTime nowUtc)
    {
        var seatLabels = string.Join(", ", booking.Seats
            .Where(s => s.Seat is not null)
            .OrderBy(s => s.Seat!.RowNumber).ThenBy(s => s.Seat!.ColumnNumber)
            .Select(s => s.Seat!.Label));
        var freed = booking.Seats.Select(s => s.SeatId).ToList();

        _db.BookingSeats.RemoveRange(booking.Seats);
        booking.Status = BookingStatus.Cancelled;
        if (booking.Payment is { Status: PaymentStatus.Succeeded } payment)
            payment.Status = PaymentStatus.RefundPending;

        var showtime = booking.Showtime!;
        var place = showtime.Room is { } room ? $"{room.Theater?.Name} · {room.RoomName}" : string.Empty;
        _db.OutboxEmails.Add(new OutboxEmail
        {
            ToAddress = booking.CustomerEmail,
            Subject = $"Vé {booking.BookingCode} đã bị hủy bởi rạp",
            Body =
                $"Xin chào {booking.CustomerName},\n\n" +
                $"Rất tiếc, rạp phải hủy vé {booking.BookingCode} của bạn.\n" +
                $"Lý do: {reason}\n\n" +
                $"Phim: {showtime.Movie?.Title}\n" +
                $"Suất chiếu: {_tz.ToLocalTime(showtime.StartTimeUtc):HH:mm dd/MM/yyyy}\n" +
                $"Rạp: {place}\n" +
                $"Ghế: {seatLabels}\n\n" +
                $"Rạp sẽ hoàn {booking.TotalAmount.Vnd()} cho bạn. Thành thật xin lỗi vì sự bất tiện này.",
            Status = OutboxStatus.Pending,
            CreatedUtc = nowUtc,
        });

        return freed;
    }

    private async Task NotifySeatsAsync(int showtimeId, List<int> seatIds, CancellationToken ct)
    {
        if (_hub is null || seatIds.Count == 0) return;
        await _hub.FreedAsync(showtimeId, seatIds);
    }
}
