using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    private readonly IBranchScope _branches;

    public RevenueController(CinemaDbContext db, IOptions<DisplayTimeZone> tz, TimeProvider clock, IBranchScope branches)
    {
        _db = db;
        _tz = tz.Value;
        _clock = clock;
        _branches = branches;
    }

    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (fromDay, toDay) = ResolveRange(from, to);
        var offsetHours = _tz.TimeZoneOffsetHours;

        var scoped = await _branches.TheaterIdAsync(ct);
        var byDay = await ConfirmedTickets(fromDay, toDay, scoped)
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

    /// <summary>Doanh thu chia theo phim và theo từng suất chiếu, lọc được theo chi nhánh.</summary>
    public async Task<IActionResult> Breakdown(DateOnly? from, DateOnly? to, int? theaterId, CancellationToken ct)
    {
        var (fromDay, toDay) = ResolveRange(from, to);
        var scoped = await _branches.TheaterIdAsync(ct);
        if (scoped is not null)
        {
            if (theaterId is not null && theaterId != scoped) return Forbid();
            theaterId = scoped;
        }
        var tickets = ConfirmedTickets(fromDay, toDay, scoped);
        if (theaterId is not null)
            tickets = tickets.Where(bs => bs.Showtime!.Room!.TheaterId == theaterId);

        var byMovie = await tickets
            .GroupBy(bs => new { bs.Showtime!.MovieId, bs.Showtime.Movie!.Title })
            .Select(g => new { g.Key.MovieId, g.Key.Title, Tickets = g.Count(), Revenue = g.Sum(x => x.PriceAtBooking) })
            .ToListAsync(ct);

        var byShowtime = await tickets
            .GroupBy(bs => new
            {
                bs.ShowtimeId,
                bs.Showtime!.Movie!.Title,
                TheaterName = bs.Showtime.Room!.Theater!.Name,
                bs.Showtime.Room.RoomName,
                bs.Showtime.RoomId,
                bs.Showtime.StartTimeUtc,
            })
            .Select(g => new
            {
                g.Key.ShowtimeId,
                g.Key.Title,
                g.Key.TheaterName,
                g.Key.RoomName,
                g.Key.RoomId,
                g.Key.StartTimeUtc,
                Tickets = g.Count(),
                Revenue = g.Sum(x => x.PriceAtBooking),
            })
            .ToListAsync(ct);

        // Sức chứa = số ghế của phòng; đếm một lần cho các phòng có mặt thay vì subquery mỗi suất.
        var roomIds = byShowtime.Select(r => r.RoomId).Distinct().ToList();
        var capacity = await _db.Seats.AsNoTracking()
            .Where(s => roomIds.Contains(s.RoomId))
            .GroupBy(s => s.RoomId)
            .Select(g => new { RoomId = g.Key, Seats = g.Count() })
            .ToDictionaryAsync(x => x.RoomId, x => x.Seats, ct);

        var movieRows = byMovie
            .Select(r => new RevenueByMovieRow(r.MovieId, r.Title, r.Tickets, r.Revenue))
            .OrderByDescending(r => r.Revenue)
            .ToList();

        var showtimeRows = byShowtime
            .Select(r => new RevenueByShowtimeRow(
                r.ShowtimeId, r.Title, r.TheaterName, r.RoomName, _tz.ToLocalTime(r.StartTimeUtc),
                r.Tickets, capacity.GetValueOrDefault(r.RoomId), r.Revenue))
            .OrderByDescending(r => r.Revenue).ThenBy(r => r.StartLocal)
            .ToList();

        var theatersQuery = _db.Theaters.AsNoTracking();
        if (scoped is not null) theatersQuery = theatersQuery.Where(t => t.Id == scoped);
        var theaters = await theatersQuery
            .OrderBy(t => t.City).ThenBy(t => t.Name)
            .Select(t => new SelectListItem(t.City + " · " + t.Name, t.Id.ToString()))
            .ToListAsync(ct);

        return View(new RevenueBreakdownViewModel(fromDay, toDay, theaterId, theaters, movieRows, showtimeRows));
    }

    /// <summary>Vé (BookingSeats) của đơn Confirmed, đặt trong [fromDay, toDay] theo giờ rạp.</summary>
    private IQueryable<BookingSeat> ConfirmedTickets(DateOnly fromDay, DateOnly toDay, int? scopedTheaterId)
    {
        var fromUtc = _tz.DayRangeUtc(fromDay).FromUtc;
        var toUtc = _tz.DayRangeUtc(toDay).ToUtc;
        var query = _db.BookingSeats.AsNoTracking()
            .Where(bs => bs.Booking!.Status == BookingStatus.Confirmed
                         && bs.Booking.BookingTimeUtc >= fromUtc
                         && bs.Booking.BookingTimeUtc < toUtc);
        return scopedTheaterId is null
            ? query
            : query.Where(bs => bs.Showtime!.Room!.TheaterId == scopedTheaterId);
    }

    private (DateOnly From, DateOnly To) ResolveRange(DateOnly? from, DateOnly? to)
    {
        var toDay = to ?? _tz.ToLocalDate(_clock.GetUtcNow().UtcDateTime);
        var fromDay = from ?? toDay.AddDays(-(DefaultDays - 1));
        return fromDay <= toDay ? (fromDay, toDay) : (toDay, fromDay);
    }
}
