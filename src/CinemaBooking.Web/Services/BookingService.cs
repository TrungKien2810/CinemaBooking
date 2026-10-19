using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Điều phối đặt vé ở phía server. Service tự đọc lại ghế từ DB và giá từ
/// <see cref="ISeatPricingService"/> — sơ đồ ghế hiển thị giá và bước ghi Booking phải dùng CÙNG
/// một công thức, hai chỗ tự tính riêng thì sớm muộn sẽ lệch nhau.
/// <para>
/// Booking + BookingSeats + Payment được ghi trong MỘT transaction: lỗi ở bất kỳ bước nào thì
/// coi như chưa có gì được ghi, không để lại đơn nửa vời trong DB.
/// </para>
/// </summary>
public sealed class BookingService : IBookingService
{
    private readonly CinemaDbContext _db;
    private readonly IBookedSeatQuery _bookedSeats;
    private readonly ISeatLockManager _locks;
    private readonly ISeatPricingService _pricing;
    private readonly IPaymentService _payment;
    private readonly IBookingCodeGenerator _codes;
    private readonly TimeProvider _clock;

    public BookingService(
        CinemaDbContext db,
        IBookedSeatQuery bookedSeats,
        ISeatLockManager locks,
        ISeatPricingService pricing,
        IPaymentService payment,
        IBookingCodeGenerator codes,
        TimeProvider clock)
    {
        _db = db;
        _bookedSeats = bookedSeats;
        _locks = locks;
        _pricing = pricing;
        _payment = payment;
        _codes = codes;
        _clock = clock;
    }

    public async Task<BookingResult> PlaceAsync(BookingRequest request, CancellationToken ct = default)
    {
        var showtime = await FindBookableShowtimeAsync(request.ShowtimeId, ct);
        if (showtime is null)
            return BookingResult.Failed(BookingResultKind.ShowtimeNotBookable, "Suất chiếu không còn mở bán.");

        var seatIds = request.SeatIds.Distinct().ToList();
        if (seatIds.Count == 0)
            return BookingResult.Failed(BookingResultKind.SeatsUnavailable, "Chưa chọn ghế nào.");

        // Ghế phải thuộc đúng phòng của suất — id gửi lên có thể là bất kỳ.
        var seats = showtime.Room!.Seats.Where(s => seatIds.Contains(s.Id)).ToList();
        if (seats.Count != seatIds.Count)
            return BookingResult.Failed(BookingResultKind.SeatsUnavailable, "Có ghế không thuộc phòng chiếu của suất này.");

        // Kiểm tra sớm cho thông báo thân thiện; chốt chặn cuối vẫn là unique index (ShowtimeId, SeatId).
        var booked = await _bookedSeats.GetBookedSeatIdsAsync(showtime.Id, ct);
        if (seatIds.Any(booked.Contains))
            return BookingResult.Failed(BookingResultKind.SeatsUnavailable, "Có ghế vừa được người khác đặt. Vui lòng chọn lại.");

        // Khách có thể ngồi ở trang thanh toán lâu hơn thời gian giữ ghế: lock RAM đã hết hạn trong
        // lúc điền form. Ghi Booking lúc này là bán ghế mà không ai còn giữ — phải chọn lại.
        if (string.IsNullOrWhiteSpace(request.ConnectionId))
            return BookingResult.Failed(BookingResultKind.LocksExpired, "Mất kết nối giữ ghế, vui lòng tải lại trang.");

        var keys = seatIds.Select(seatId => new SeatKey(showtime.Id, seatId)).ToList();
        if (!_locks.ValidateLocks(keys, request.ConnectionId))
            return BookingResult.Failed(BookingResultKind.LocksExpired, "Thời gian giữ ghế đã hết hoặc ghế không còn do bạn giữ. Vui lòng chọn lại.");

        var booking = BuildBooking(request, showtime, seats);

        // Transaction 1: giữ chỗ. Suất chiếu đã bắt đầu hay chưa được kiểm lại ở trên; đơn Pending
        // + unique index chính là thứ giữ ghế trong lúc khách thanh toán (General.md §7.5) — mất
        // kết nối SignalR ở bước này cũng không làm mất ghế.
        await using (var transaction = await _db.Database.BeginTransactionAsync(ct))
        {
            _db.Bookings.Add(booking);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsDuplicateSeat(ex))
            {
                // Ai đó thắng cuộc đua giữa lúc kiểm ghế và lúc ghi: unique index (ShowtimeId, SeatId) từ chối.
                // Đây là chốt chặn cuối (General.md §5.1 mục 2) — trả lỗi thân thiện, không để lộ 500.
                _db.ChangeTracker.Clear();
                return BookingResult.Failed(BookingResultKind.SeatsUnavailable, "Có ghế vừa bị người khác đặt. Vui lòng chọn lại.");
            }

            await transaction.CommitAsync(ct);
        }

        var charge = await _payment.ChargeAsync(
            new PaymentRequest(booking.BookingCode, booking.TotalAmount, booking.CustomerEmail), ct);

        if (!charge.Succeeded)
        {
            // Đơn không thành: xóa dòng BookingSeats để trả ghế ngay (unique index không lọc theo trạng
            // thái đơn, nên hủy vé phải xóa dòng ghế), giữ lại Booking làm lịch sử.
            _db.BookingSeats.RemoveRange(booking.Seats);
            booking.Status = BookingStatus.Cancelled;
            booking.Payment!.Status = PaymentStatus.Failed;
            await _db.SaveChangesAsync(ct);

            return BookingResult.Failed(BookingResultKind.PaymentFailed,
                charge.FailureReason ?? "Thanh toán không thành công. Vui lòng thử lại.");
        }

        booking.Status = BookingStatus.Confirmed;
        booking.Payment!.Status = PaymentStatus.Succeeded;
        booking.Payment.TransactionId = charge.TransactionId;

        // Email KHÔNG gửi trực tiếp trong request (fire-and-forget sẽ mất mail nếu process chết):
        // Web chỉ ghi vào outbox cùng transaction với bước xác nhận, EmailOutboxWorker mới gửi.
        _db.OutboxEmails.Add(BuildConfirmationEmail(booking, showtime, seats));

        await _db.SaveChangesAsync(ct);

        // Ghế đã thành "đã đặt" ở DB: nhả lock RAM để nhường chỗ cho lần mua sau.
        foreach (var key in keys) _locks.Release(key, request.ConnectionId);

        return BookingResult.Success(booking.BookingCode);
    }

    /// <summary>
    /// Nhận diện vi phạm unique index (ShowtimeId, SeatId). SQL Server: "Cannot insert duplicate key
    /// ... 'BookingSeats'"; SQLite: "UNIQUE constraint failed: BookingSeats...". Dò theo thông điệp
    /// để chạy được trên cả hai provider, không phụ thuộc mã lỗi riêng của từng DB.
    /// </summary>
    private static bool IsDuplicateSeat(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("BookingSeats", StringComparison.OrdinalIgnoreCase)
            && (message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    private Booking BuildBooking(BookingRequest request, Showtime showtime, IReadOnlyList<Seat> seats)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var priced = seats
            .Select(seat => (Seat: seat, Price: _pricing.PriceFor(showtime.BasePrice, seat.SeatType)))
            .ToList();
        var total = priced.Sum(x => x.Price);

        return new Booking
        {
            BookingCode = _codes.Next(),
            UserId = request.UserId,
            ShowtimeId = showtime.Id,
            CustomerName = request.CustomerName.Trim(),
            CustomerEmail = request.CustomerEmail.Trim(),
            CustomerPhone = string.IsNullOrWhiteSpace(request.CustomerPhone) ? null : request.CustomerPhone.Trim(),
            BookingTimeUtc = nowUtc,
            TotalAmount = total,
            Status = BookingStatus.Pending,
            Seats = priced.Select(x => new BookingSeat
            {
                SeatId = x.Seat.Id,
                ShowtimeId = showtime.Id,
                PriceAtBooking = x.Price,
            }).ToList(),
            Payment = new Payment
            {
                Amount = total,
                PaymentMethod = "Mock",
                Status = PaymentStatus.Pending,
                CreatedUtc = nowUtc,
            },
        };
    }

    /// <summary>
    /// Thư xác nhận chỉ là dữ liệu trong outbox; nội dung hiển thị ở đây phải đủ để khách đọc lại
    /// mà không cần mở DB (mã vé, phim, suất, ghế, số tiền).
    /// </summary>
    private static OutboxEmail BuildConfirmationEmail(Booking booking, Showtime showtime, IReadOnlyList<Seat> seats)
    {
        var labels = string.Join(", ", seats
            .OrderBy(s => s.RowNumber).ThenBy(s => s.ColumnNumber)
            .Select(s => s.Label));

        var body =
            $"Xin chào {booking.CustomerName},\n\n" +
            $"Đơn {booking.BookingCode} của bạn đã được xác nhận.\n" +
            $"Phim: {showtime.Movie?.Title}\n" +
            $"Suất chiếu: {showtime.StartTimeUtc:HH:mm dd/MM/yyyy} (UTC)\n" +
            $"Ghế: {labels}\n" +
            $"Tổng tiền: {booking.TotalAmount:N0} VND\n\n" +
            "Cảm ơn bạn đã đặt vé tại CinemaBooking.";

        return new OutboxEmail
        {
            ToAddress = booking.CustomerEmail,
            Subject = $"Xác nhận đặt vé {booking.BookingCode}",
            Body = body,
            Status = OutboxStatus.Pending,
        };
    }

    /// <summary>
    /// Cùng điều kiện "suất còn bán" với <c>BookingController.FindBookableShowtimeAsync</c>:
    /// suất chưa bắt đầu, phim và chi nhánh chưa ẩn.
    /// </summary>
    private Task<Showtime?> FindBookableShowtimeAsync(int showtimeId, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        return _db.Showtimes
            .Include(s => s.Movie)
            .Include(s => s.Room!).ThenInclude(r => r.Seats)
            .FirstOrDefaultAsync(s => s.Id == showtimeId
                && s.StartTimeUtc > nowUtc
                && s.Movie!.IsActive
                && s.Room!.Theater!.IsActive, ct);
    }
}
