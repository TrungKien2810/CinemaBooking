using CinemaBooking.Domain;
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

/// <summary>Tra cứu đơn đặt vé: Admin thấy mọi chi nhánh, chủ chi nhánh chỉ thấy chi nhánh mình: lọc theo mã, chi nhánh, trạng thái; xem chi tiết.</summary>
public class BookingsController : AdminControllerBase
{
    /// <summary>Số đơn tối đa trên một trang danh sách — đơn mới nhất trước, lọc để thu hẹp.</summary>
    public const int MaxItems = 100;

    private readonly CinemaDbContext _db;
    private readonly DisplayTimeZone _tz;
    private readonly IBranchScope _branches;

    public BookingsController(CinemaDbContext db, IOptions<DisplayTimeZone> tz, IBranchScope branches)
    {
        _db = db;
        _tz = tz.Value;
        _branches = branches;
    }

    public async Task<IActionResult> Index(string? code, int? theaterId, BookingStatus? status, CancellationToken ct)
    {
        var scoped = await _branches.TheaterIdAsync(ct);
        if (scoped is not null)
        {
            if (theaterId is not null && theaterId != scoped) return Forbid();
            theaterId = scoped;
        }

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

        var theatersQuery = _db.Theaters.AsNoTracking();
        if (scoped is not null) theatersQuery = theatersQuery.Where(t => t.Id == scoped);
        var theaters = await theatersQuery
            .OrderBy(t => t.City).ThenBy(t => t.Name)
            .Select(t => new SelectListItem(t.City + " · " + t.Name, t.Id.ToString()))
            .ToListAsync(ct);

        return View(new BookingsIndexViewModel(code, theaterId, status, theaters, items, MaxItems));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var b = await _db.Bookings.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.BookingCode,
                x.Status,
                x.BookingTimeUtc,
                x.CustomerName,
                x.CustomerEmail,
                x.CustomerPhone,
                x.Showtime!.Movie!.Title,
                TheaterName = x.Showtime.Room!.Theater!.Name,
                x.Showtime.Room.RoomName,
                x.Showtime.Room.TheaterId,
                x.Showtime.StartTimeUtc,
                x.TotalAmount,
                PaymentStatus = (PaymentStatus?)x.Payment!.Status,
                x.Payment.TransactionId,
                Seats = x.Seats
                    .OrderBy(s => s.Seat!.RowNumber).ThenBy(s => s.Seat!.ColumnNumber)
                    .Select(s => new { s.Seat, s.PriceAtBooking })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (b is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(b.TheaterId, ct)) return Forbid();

        var seats = b.Seats
            .Select(s => new BookingSeatLine(s.Seat!.Label, s.Seat.SeatType, s.PriceAtBooking))
            .ToList();

        return View(new BookingDetailsViewModel(
            b.Id, b.BookingCode, b.Status, _tz.ToLocalTime(b.BookingTimeUtc),
            b.CustomerName, b.CustomerEmail, b.CustomerPhone,
            b.Title, b.TheaterName, b.RoomName, _tz.ToLocalTime(b.StartTimeUtc),
            b.TotalAmount, b.PaymentStatus, b.TransactionId, seats));
    }
}
