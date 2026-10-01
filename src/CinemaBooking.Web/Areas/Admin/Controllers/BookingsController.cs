using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>Tra cứu đơn đặt vé của mọi chi nhánh: lọc theo mã, chi nhánh, trạng thái.</summary>
public class BookingsController : AdminControllerBase
{
    /// <summary>Số đơn tối đa trên một trang danh sách — đơn mới nhất trước, lọc để thu hẹp.</summary>
    public const int MaxItems = 100;

    private readonly CinemaDbContext _db;
    private readonly DisplayTimeZone _tz;

    public BookingsController(CinemaDbContext db, IOptions<DisplayTimeZone> tz)
    {
        _db = db;
        _tz = tz.Value;
    }

    public async Task<IActionResult> Index(string? code, int? theaterId, BookingStatus? status, CancellationToken ct)
    {
        var query = _db.Bookings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(code))
        {
            var normalized = code.Trim().ToUpperInvariant();
            query = query.Where(b => b.BookingCode.Contains(normalized));
        }
        if (theaterId is not null)
            query = query.Where(b => b.Showtime!.Room!.TheaterId == theaterId);
        if (status is not null)
            query = query.Where(b => b.Status == status);

        var rows = await query
            .OrderByDescending(b => b.BookingTimeUtc)
            .Take(MaxItems)
            .Select(b => new
            {
                b.Id,
                b.BookingCode,
                b.CustomerName,
                b.Showtime!.Movie!.Title,
                TheaterName = b.Showtime.Room!.Theater!.Name,
                b.Showtime.StartTimeUtc,
                b.BookingTimeUtc,
                SeatCount = b.Seats.Count,
                b.TotalAmount,
                b.Status,
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new BookingAdminItem(
                r.Id, r.BookingCode, r.CustomerName, r.Title, r.TheaterName,
                _tz.ToLocalTime(r.StartTimeUtc), _tz.ToLocalTime(r.BookingTimeUtc),
                r.SeatCount, r.TotalAmount, r.Status))
            .ToList();

        var theaters = await _db.Theaters.AsNoTracking()
            .OrderBy(t => t.City).ThenBy(t => t.Name)
            .Select(t => new SelectListItem(t.City + " · " + t.Name, t.Id.ToString()))
            .ToListAsync(ct);

        return View(new BookingsIndexViewModel(code, theaterId, status, theaters, items, MaxItems));
    }
}
