using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>
/// Thống kê doanh thu, tính thẳng bằng LINQ trên vé của đơn đã xác nhận — luôn khớp dữ liệu đơn
/// tới từng phút, không phụ thuộc DailyReportWorker đã chạy hay chưa. Một ngày = một ngày theo giờ
/// rạp của thời điểm ĐẶT vé (lúc thu tiền), không phải ngày chiếu.
/// </summary>
public class RevenueController : AdminControllerBase
{
    /// <summary>Mặc định xem 7 ngày gần nhất, tính cả hôm nay.</summary>
    public const int DefaultDays = 7;

    private readonly CinemaDbContext _db;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;

    public RevenueController(CinemaDbContext db, IOptions<DisplayTimeZone> tz, TimeProvider clock)
    {
        _db = db;
        _tz = tz.Value;
        _clock = clock;
    }

    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (fromDay, toDay) = ResolveRange(from, to);
        var offsetHours = _tz.TimeZoneOffsetHours;

        var byDay = await ConfirmedTickets(fromDay, toDay)
            .GroupBy(bs => new
            {
                Day = bs.Booking!.BookingTimeUtc.AddHours(offsetHours).Date,
                bs.Showtime!.Room!.TheaterId,
                bs.Showtime.Room.Theater!.Name,
            })
            .Select(g => new { g.Key.Day, g.Key.TheaterId, g.Key.Name, Tickets = g.Count(), Revenue = g.Sum(x => x.PriceAtBooking) })
            .ToListAsync(ct);

        var dayRows = byDay
            .Select(r => new RevenueByDayRow(DateOnly.FromDateTime(r.Day), r.TheaterId, r.Name, r.Tickets, r.Revenue))
            .OrderByDescending(r => r.Day).ThenBy(r => r.TheaterName)
            .ToList();

        var theaterRows = dayRows
            .GroupBy(r => (r.TheaterId, r.TheaterName))
            .Select(g => new RevenueByTheaterRow(g.Key.TheaterId, g.Key.TheaterName, g.Sum(r => r.Tickets), g.Sum(r => r.Revenue)))
            .OrderByDescending(r => r.Revenue)
            .ToList();

        return View(new RevenueDashboardViewModel(fromDay, toDay, dayRows, theaterRows));
    }

    /// <summary>Vé (BookingSeats) của đơn Confirmed, đặt trong [fromDay, toDay] theo giờ rạp.</summary>
    private IQueryable<BookingSeat> ConfirmedTickets(DateOnly fromDay, DateOnly toDay)
    {
        var fromUtc = _tz.DayRangeUtc(fromDay).FromUtc;
        var toUtc = _tz.DayRangeUtc(toDay).ToUtc;
        return _db.BookingSeats.AsNoTracking()
            .Where(bs => bs.Booking!.Status == BookingStatus.Confirmed
                         && bs.Booking.BookingTimeUtc >= fromUtc
                         && bs.Booking.BookingTimeUtc < toUtc);
    }

    private (DateOnly From, DateOnly To) ResolveRange(DateOnly? from, DateOnly? to)
    {
        var toDay = to ?? _tz.ToLocalDate(_clock.GetUtcNow().UtcDateTime);
        var fromDay = from ?? toDay.AddDays(-(DefaultDays - 1));
        return fromDay <= toDay ? (fromDay, toDay) : (toDay, fromDay);
    }
}
