using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>
/// Trang tổng quan hôm nay: suất chiếu, vé bán, doanh thu, tỉ lệ lấp đầy và các suất sắp chiếu.
/// Chủ chi nhánh chỉ thấy số liệu chi nhánh mình (phạm vi đọc từ DB qua <see cref="IBranchScope"/>).
/// </summary>
public class DashboardController : AdminControllerBase
{
    private const int UpcomingLimit = 10;

    private readonly CinemaDbContext _db;
    private readonly IBranchScope _branches;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;

    public DashboardController(CinemaDbContext db, IBranchScope branches, IOptions<DisplayTimeZone> tz, TimeProvider clock)
    {
        _db = db;
        _branches = branches;
        _tz = tz.Value;
        _clock = clock;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var scoped = await _branches.TheaterIdAsync(ct);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var today = _tz.ToLocalDate(nowUtc);
        var (dayFrom, dayTo) = _tz.DayRangeUtc(today);
        var weekFrom = _tz.DayRangeUtc(today.AddDays(-6)).FromUtc;

        var scope = scoped is null
            ? "Toàn hệ thống"
            : await _db.Theaters.Where(t => t.Id == scoped).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? "Chi nhánh";

        var showtimes = _db.Showtimes.AsNoTracking().Where(s => scoped == null || s.Room!.TheaterId == scoped);
        var tickets = _db.BookingSeats.AsNoTracking()
            .Where(bs => bs.Booking!.Status == BookingStatus.Confirmed)
            .Where(bs => scoped == null || bs.Showtime!.Room!.TheaterId == scoped);

        var showtimesToday = await showtimes.CountAsync(s => s.StartTimeUtc >= dayFrom && s.StartTimeUtc < dayTo && s.IsActive, ct);

        var soldToday = tickets.Where(bs => bs.Booking!.BookingTimeUtc >= dayFrom && bs.Booking.BookingTimeUtc < dayTo);
        var ticketsToday = await soldToday.CountAsync(ct);
        var revenueToday = await soldToday.SumAsync(bs => (decimal?)bs.PriceAtBooking, ct) ?? 0m;

        var sold7 = tickets.Where(bs => bs.Booking!.BookingTimeUtc >= weekFrom && bs.Booking.BookingTimeUtc < dayTo);
        var tickets7 = await sold7.CountAsync(ct);
        var revenue7 = await sold7.SumAsync(bs => (decimal?)bs.PriceAtBooking, ct) ?? 0m;

        // Lấp đầy hôm nay = vé đã bán / tổng ghế của các suất chiếu hôm nay (mọi suất đang bán).
        var todayRows = await showtimes
            .Where(s => s.StartTimeUtc >= dayFrom && s.StartTimeUtc < dayTo && s.IsActive)
            .Select(s => new { Sold = _db.BookingSeats.Count(b => b.ShowtimeId == s.Id && b.Booking!.Status == BookingStatus.Confirmed), Capacity = s.Room!.Seats.Count })
            .ToListAsync(ct);
        var capacity = todayRows.Sum(r => r.Capacity);
        var occupancy = capacity == 0 ? 0 : (double)todayRows.Sum(r => r.Sold) / capacity;

        var upcomingRaw = await showtimes
            .Where(s => s.StartTimeUtc >= nowUtc && s.StartTimeUtc < dayTo)
            .OrderBy(s => s.StartTimeUtc)
            .Take(UpcomingLimit)
            .Select(s => new
            {
                s.Id, s.Movie!.Title, TheaterName = s.Room!.Theater!.Name, s.Room.RoomName, s.StartTimeUtc, s.IsActive,
                Sold = _db.BookingSeats.Count(b => b.ShowtimeId == s.Id && b.Booking!.Status == BookingStatus.Confirmed),
                Capacity = s.Room.Seats.Count,
            })
            .ToListAsync(ct);

        var upcoming = upcomingRaw
            .Select(r => new DashboardShowtimeRow(r.Id, r.Title, r.TheaterName, r.RoomName, _tz.ToLocalTime(r.StartTimeUtc), r.Sold, r.Capacity, r.IsActive))
            .ToList();

        return View(new DashboardViewModel(scope, today, showtimesToday, ticketsToday, revenueToday, tickets7, revenue7, occupancy, upcoming));
    }
}
