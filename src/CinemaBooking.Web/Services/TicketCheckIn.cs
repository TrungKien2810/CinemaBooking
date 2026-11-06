using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

public enum CheckInOutcome
{
    /// <summary>Hợp lệ, có thể cho vào rạp.</summary>
    Ok,
    NotFound,
    /// <summary>Đơn chưa thanh toán / đã hủy / hết hạn.</summary>
    NotPaid,
    /// <summary>Quá sớm: cổng soát vé chỉ mở trước giờ chiếu một khoảng.</summary>
    TooEarly,
    /// <summary>Suất chiếu đã kết thúc.</summary>
    ShowEnded,
    /// <summary>Vé đã được soát — không cho vào lần hai.</summary>
    AlreadyCheckedIn,
}

/// <summary>Quy tắc soát vé tại rạp. Thuần hàm để test không cần DB.</summary>
public static class TicketCheckInRules
{
    /// <summary>Cổng soát vé mở trước giờ chiếu bấy nhiêu phút.</summary>
    public static readonly TimeSpan OpensBefore = TimeSpan.FromMinutes(60);

    public static CheckInOutcome Evaluate(Booking booking, DateTime nowUtc)
    {
        if (booking.Status != BookingStatus.Confirmed) return CheckInOutcome.NotPaid;
        if (booking.CheckedInUtc is not null) return CheckInOutcome.AlreadyCheckedIn;

        var showtime = booking.Showtime!;
        if (nowUtc >= showtime.EndTimeUtc) return CheckInOutcome.ShowEnded;
        if (nowUtc < showtime.StartTimeUtc - OpensBefore) return CheckInOutcome.TooEarly;

        return CheckInOutcome.Ok;
    }
}

public interface ITicketCheckInService
{
    /// <summary>Tìm đơn theo mã (kèm suất, phim, rạp, ghế). Null nếu không có.</summary>
    Task<Booking?> FindAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Ghi nhận soát vé. Chỉ ghi khi đơn vẫn hợp lệ ở thời điểm ghi: hai nhân viên quét cùng lúc một vé
    /// thì đúng một người thắng (cập nhật có điều kiện trong DB), người kia nhận <see cref="CheckInOutcome.AlreadyCheckedIn"/>.
    /// </summary>
    Task<CheckInOutcome> CheckInAsync(string code, string staffEmail, CancellationToken ct = default);
}

public sealed class TicketCheckInService : ITicketCheckInService
{
    private readonly CinemaDbContext _db;
    private readonly TimeProvider _clock;

    public TicketCheckInService(CinemaDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task<Booking?> FindAsync(string code, CancellationToken ct = default)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        return _db.Bookings.AsNoTracking()
            .Include(b => b.Showtime!).ThenInclude(s => s.Movie)
            .Include(b => b.Showtime!).ThenInclude(s => s.Room!).ThenInclude(r => r.Theater)
            .Include(b => b.Seats).ThenInclude(bs => bs.Seat)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.BookingCode == normalized, ct);
    }

    public async Task<CheckInOutcome> CheckInAsync(string code, string staffEmail, CancellationToken ct = default)
    {
        var booking = await FindAsync(code, ct);
        if (booking is null) return CheckInOutcome.NotFound;

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var outcome = TicketCheckInRules.Evaluate(booking, nowUtc);
        if (outcome != CheckInOutcome.Ok) return outcome;

        // Điều kiện CheckedInUtc IS NULL nằm trong chính câu UPDATE: người đến sau thấy 0 dòng bị đổi.
        var updated = await _db.Bookings
            .Where(b => b.Id == booking.Id && b.CheckedInUtc == null && b.Status == BookingStatus.Confirmed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.CheckedInUtc, nowUtc)
                .SetProperty(b => b.CheckedInBy, staffEmail), ct);

        return updated == 1 ? CheckInOutcome.Ok : CheckInOutcome.AlreadyCheckedIn;
    }
}
