using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>Tra cứu đơn đặt vé của mọi chi nhánh: lọc theo mã, chi nhánh, trạng thái; xem chi tiết và hủy.</summary>
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
    /// Hủy đơn và trả ghế. "Ghế đã đặt" = có dòng BookingSeats của suất (AD-5), nên trả ghế là XÓA
    /// các dòng đó — đổi trạng thái thôi thì unique index vẫn chặn người khác đặt lại ghế.
    /// Thanh toán đã thành công thì đánh dấu hoàn tiền. Một lần SaveChanges = một transaction.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var booking = await _db.Bookings
            .Include(b => b.Seats)
            .Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();

        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            TempData["Error"] = "Đơn này đã hủy hoặc hết hạn, không còn giữ ghế nào.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var freedSeats = booking.Seats.Count;
        booking.Status = BookingStatus.Cancelled;
        _db.BookingSeats.RemoveRange(booking.Seats);
        if (booking.Payment is { Status: PaymentStatus.Succeeded })
            booking.Payment.Status = PaymentStatus.Refunded;

        await _db.SaveChangesAsync(ct);

        TempData["Message"] = $"Đã hủy đơn {booking.BookingCode} và trả {freedSeats} ghế.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
