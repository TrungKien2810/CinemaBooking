using System.Security.Claims;
using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Hubs;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Controllers;

/// <summary>
/// Luồng đặt vé phía khách. Trang chọn ghế trả về sơ đồ + ghế đã đặt (đọc từ DB);
/// ghế ĐANG GIỮ thì client nhận qua SeatHub (SeatSnapshot), không đi qua controller.
/// </summary>
public class BookingController : Controller
{
    private readonly CinemaDbContext _db;
    private readonly IBookedSeatQuery _bookedSeats;
    private readonly ISeatLockManager _locks;
    private readonly ISeatPricingService _pricing;
    private readonly IBookingService _bookings;
    private readonly IHubContext<SeatHub, ISeatClient> _seats;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;
    private readonly SeatHoldOptions _hold;

    public BookingController(
        CinemaDbContext db,
        IBookedSeatQuery bookedSeats,
        ISeatLockManager locks,
        ISeatPricingService pricing,
        IBookingService bookings,
        IHubContext<SeatHub, ISeatClient> seats,
        IOptions<DisplayTimeZone> tz,
        TimeProvider clock,
        IOptions<SeatHoldOptions> hold)
    {
        _db = db;
        _bookedSeats = bookedSeats;
        _locks = locks;
        _pricing = pricing;
        _bookings = bookings;
        _seats = seats;
        _tz = tz.Value;
        _clock = clock;
        _hold = hold.Value;
    }

    [HttpGet]
    public async Task<IActionResult> SeatMap(int id, CancellationToken ct)
    {
        var showtime = await FindBookableShowtimeAsync(id, ct);
        if (showtime is null) return NotFound();

        var booked = await _bookedSeats.GetBookedSeatIdsAsync(showtime.Id, ct);

        return View(SeatMapViewModel.Build(
            showtime, booked, _pricing, _tz.ToLocalTime(showtime.StartTimeUtc), (int)_hold.Duration.TotalSeconds));
    }

    /// <summary>
    /// Chốt các ghế khách đang giữ vào Session rồi sang trang thanh toán. Kiểm tra lại toàn bộ
    /// phía server: ghế thuộc đúng phòng của suất, chưa ai đặt, và đang bị CHÍNH connection này
    /// giữ trong SeatLockManager — form có thể bị sửa để gửi ghế bất kỳ.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectSeats(int showtimeId, int[] seatIds, string? connectionId, CancellationToken ct)
    {
        var showtime = await FindBookableShowtimeAsync(showtimeId, ct);
        if (showtime is null) return NotFound();

        var ids = seatIds.Distinct().ToList();
        var error = await ValidateSelectionAsync(showtime, ids, connectionId, ct);
        if (error is not null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(SeatMap), new { id = showtimeId });
        }

        HttpContext.Session.SetSeatSelection(new SeatSelection(showtimeId, ids, connectionId!));
        return RedirectToAction(nameof(Checkout));
    }

    /// <summary>Tóm tắt giỏ ghế trong Session. Form thông tin khách và thanh toán thuộc Sprint 4.</summary>
    [HttpGet]
    public async Task<IActionResult> Checkout(CancellationToken ct)
    {
        var selection = HttpContext.Session.GetSeatSelection();
        if (selection is null) return RedirectToAction("Index", "Home");

        var showtime = await FindBookableShowtimeAsync(selection.ShowtimeId, ct);
        if (showtime is null)
        {
            HttpContext.Session.ClearSeatSelection();
            return NotFound();
        }

        var seats = showtime.Room!.Seats
            .Where(s => selection.SeatIds.Contains(s.Id))
            .OrderBy(s => s.RowNumber).ThenBy(s => s.ColumnNumber)
            .Select(s => new CheckoutSeatItem(s.Label, s.SeatType))
            .ToList();

        return View(new CheckoutViewModel(
            showtime.Id, showtime.Movie!.Title, showtime.Room.Theater!.Name, showtime.Room.RoomName,
            _tz.ToLocalTime(showtime.StartTimeUtc), seats));
    }

    /// <summary>
    /// Chốt đơn: tạo Booking trong một transaction, thu tiền và xếp email xác nhận (xem
    /// <see cref="IBookingService"/>). Danh sách ghế lấy lại từ Session, không đọc từ form.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(CheckoutFormModel form, CancellationToken ct)
    {
        var selection = HttpContext.Session.GetSeatSelection();
        if (selection is null) return RedirectToAction("Index", "Home");

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Vui lòng kiểm tra lại thông tin liên hệ.";
            return RedirectToAction(nameof(Checkout));
        }

        var result = await _bookings.PlaceAsync(new BookingRequest(
            selection.ShowtimeId,
            selection.SeatIds,
            selection.ConnectionId,
            form.CustomerName,
            form.CustomerEmail,
            form.CustomerPhone,
            User.FindFirstValue(ClaimTypes.NameIdentifier)), ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Checkout));
        }

        // Ghế đã được đặt: báo cho mọi máy đang mở suất này vẽ lại thành "đã đặt" (General.md §6.2 bước 5).
        await _seats.Clients.Group(SeatHub.GroupName(selection.ShowtimeId)).SeatBooked(selection.SeatIds.ToList());

        HttpContext.Session.ClearSeatSelection();
        TempData["BookingCode"] = result.BookingCode;
        return RedirectToAction("Index", "Home");
    }

    private async Task<string?> ValidateSelectionAsync(Showtime showtime, List<int> ids, string? connectionId, CancellationToken ct)
    {
        if (ids.Count == 0) return "Chưa chọn ghế nào.";
        if (string.IsNullOrWhiteSpace(connectionId)) return "Mất kết nối giữ ghế, vui lòng tải lại trang.";

        var inRoom = showtime.Room!.Seats.Count(s => ids.Contains(s.Id));
        if (inRoom != ids.Count) return "Có ghế không thuộc phòng chiếu của suất này.";

        var booked = await _bookedSeats.GetBookedSeatIdsAsync(showtime.Id, ct);
        if (ids.Any(booked.Contains)) return "Có ghế vừa được người khác đặt.";

        var keys = ids.Select(seatId => new SeatKey(showtime.Id, seatId));
        if (!_locks.ValidateLocks(keys, connectionId))
            return "Thời gian giữ ghế đã hết hoặc ghế không còn do bạn giữ. Vui lòng chọn lại.";

        return null;
    }

    /// <summary>
    /// Suất không tồn tại, đã bắt đầu, hoặc phim/chi nhánh đã ẩn thì coi như không có — id trên
    /// URL/form do khách gõ được, không được giữ ghế cho suất không còn bán.
    /// </summary>
    private Task<Showtime?> FindBookableShowtimeAsync(int id, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        return _db.Showtimes.AsNoTracking()
            .Include(s => s.Movie)
            .Include(s => s.Room!).ThenInclude(r => r.Theater)
            .Include(s => s.Room!).ThenInclude(r => r.Seats)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id && s.StartTimeUtc > nowUtc
                                      && s.Movie!.IsActive && s.Room!.Theater!.IsActive, ct);
    }
}
