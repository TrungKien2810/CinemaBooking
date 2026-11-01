using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Hubs;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>Tra cứu đơn đặt vé của mọi chi nhánh: lọc theo mã, chi nhánh, trạng thái; xem chi tiết và hủy.</summary>
public class BookingsController : AdminControllerBase
{
    /// <summary>Số đơn tối đa trên một trang danh sách — đơn mới nhất trước, lọc để thu hẹp.</summary>
    public const int MaxItems = 100;

    private readonly CinemaDbContext _db;
    private readonly DisplayTimeZone _tz;
    private readonly IBookingCancellationService _cancellations;
    private readonly IHubContext<SeatHub, ISeatClient>? _seats;

    public BookingsController(
        CinemaDbContext db,
        IOptions<DisplayTimeZone> tz,
        IBookingCancellationService cancellations,
        IHubContext<SeatHub, ISeatClient>? seats = null)
    {
        _db = db;
        _tz = tz.Value;
        _cancellations = cancellations;
        _seats = seats;
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

        var seats = b.Seats
            .Select(s => new BookingSeatLine(s.Seat!.Label, s.Seat.SeatType, s.PriceAtBooking))
            .ToList();

        return View(new BookingDetailsViewModel(
            b.Id, b.BookingCode, b.Status, _tz.ToLocalTime(b.BookingTimeUtc),
            b.CustomerName, b.CustomerEmail, b.CustomerPhone,
            b.Title, b.TheaterName, b.RoomName, _tz.ToLocalTime(b.StartTimeUtc),
            b.TotalAmount, b.PaymentStatus, b.TransactionId, seats));
    }

    /// <summary>
    /// Hủy đơn và trả ghế. Dùng chung <see cref="IBookingCancellationService"/> với khách để mọi
    /// đường hủy đều đi qua cùng quy tắc hoàn tiền VNPAY, outbox email và trạng thái Payment.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var booking = await _db.Bookings.AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new { b.BookingCode })
            .FirstOrDefaultAsync(ct);
        if (booking is null) return NotFound();

        var adminId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var result = await _cancellations.CancelAsync(new CancelBookingRequest(
            BookingCode: booking.BookingCode,
            UserId: adminId,
            Email: null,
            IsAdmin: true,
            Actor: $"admin:{adminId}"), ct);

        if (result.IsSuccess)
        {
            if (_seats is not null && result.ShowtimeId > 0 && result.SeatIds is { Count: > 0 })
            {
                await _seats.Clients.Group(SeatHub.GroupName(result.ShowtimeId))
                    .SeatUnlocked(result.SeatIds);
            }

            TempData["Message"] = result.Message;
        }
        else
        {
            TempData["Error"] = result.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }
}
