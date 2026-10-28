using System.Security.Claims;
using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Hubs;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Authorization;
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

        // Quay lại sơ đồ ghế sau khi đã sang Checkout nghĩa là khách đổi ý: nhả hold cũ
        // (đang thuộc hold-token trong Session) để ghế không hiện nhầm là "người khác giữ".
        var previous = HttpContext.Session.GetSeatSelection();
        if (previous is not null && previous.ShowtimeId == id)
        {
            var released = previous.SeatIds
                .Where(seatId => _locks.Release(new SeatKey(id, seatId), previous.ConnectionId))
                .ToList();

            HttpContext.Session.ClearSeatSelection();

            if (released.Count > 0)
                await _seats.Clients.Group(SeatHub.GroupName(id)).SeatUnlocked(released);
        }

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

        // Rời trang chọn ghế sẽ đóng connection SignalR. Chuyển lock sang một hold-token
        // trong Session để disconnect không nhả ghế trước khi khách kịp thanh toán.
        var holdToken = $"hold:{Guid.NewGuid():N}";
        var keys = ids.Select(seatId => new SeatKey(showtimeId, seatId)).ToList();
        if (!_locks.TransferOwnership(keys, connectionId!, holdToken, _hold.Duration))
        {
            TempData["Error"] = "Thời gian giữ ghế đã hết hoặc ghế không còn do bạn giữ. Vui lòng chọn lại.";
            return RedirectToAction(nameof(SeatMap), new { id = showtimeId });
        }

        HttpContext.Session.SetSeatSelection(new SeatSelection(showtimeId, ids, holdToken));
        return RedirectToAction(nameof(Checkout));
    }

    /// <summary>
    /// Tóm tắt giỏ ghế trong Session + form liên hệ. Khách đã đăng nhập được điền sẵn
    /// họ tên/email để không phải gõ lại.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Checkout(CancellationToken ct)
    {
        var selection = HttpContext.Session.GetSeatSelection();
        if (selection is null) return RedirectToAction("Index", "Home");

        var page = await BuildCheckoutPageAsync(selection, await BuildCheckoutFormAsync(ct), ct);
        if (page is null) return RedirectToAction("Index", "Home");

        return View(page);
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

        // Giữ lại form + lỗi từng trường thay vì redirect làm mất ModelState.
        if (!ModelState.IsValid)
        {
            var invalid = await BuildCheckoutPageAsync(selection, form, ct);
            if (invalid is null) return RedirectToAction("Index", "Home");
            return View(invalid);
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
        return RedirectToAction(nameof(Confirmation), new { code = result.BookingCode });
    }

    /// <summary>Trang xác nhận sau khi đặt vé — mã vé là thứ khách cần lưu lại.</summary>
    [HttpGet]
    public async Task<IActionResult> Confirmation(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return View("BookingNotFound", (string?)null);

        var booking = await FindBookingAsync(code, ct);
        if (booking is null)
            return View("BookingNotFound", code.Trim());

        return View(ToDetails(booking));
    }

    /// <summary>Tra cứu vé theo mã, dùng được cho cả khách không đăng nhập.</summary>
    [HttpGet]
    public async Task<IActionResult> Lookup(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return View(new BookingLookupViewModel(null, null, null));

        var booking = await FindBookingAsync(code, ct);
        if (booking is null)
        {
            return View(new BookingLookupViewModel(
                code.Trim(),
                "Không tìm thấy vé với mã này. Vui lòng kiểm tra lại email xác nhận.",
                null));
        }

        return View(new BookingLookupViewModel(booking.BookingCode, null, ToDetails(booking)));
    }

    /// <summary>Vé của tài khoản đang đăng nhập, mới nhất trước.</summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> MyBookings(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var bookings = await _db.Bookings.AsNoTracking()
            .Where(b => b.UserId == userId)
            .Include(b => b.Showtime!).ThenInclude(s => s.Movie)
            .Include(b => b.Showtime!).ThenInclude(s => s.Room!).ThenInclude(r => r.Theater)
            .Include(b => b.Seats).ThenInclude(bs => bs.Seat)
            .Include(b => b.Payment)
            .AsSplitQuery()
            .OrderByDescending(b => b.BookingTimeUtc)
            .ToListAsync(ct);

        return View(new MyBookingsViewModel(bookings.Select(ToDetails).ToList()));
    }

    /// <summary>Dựng lại tóm tắt đơn từ Session; trả null nếu suất không còn bán (đã xóa Session).</summary>
    private async Task<CheckoutPageViewModel?> BuildCheckoutPageAsync(
        SeatSelection selection, CheckoutFormModel form, CancellationToken ct)
    {
        var showtime = await FindBookableShowtimeAsync(selection.ShowtimeId, ct);
        if (showtime is null)
        {
            HttpContext.Session.ClearSeatSelection();
            return null;
        }

        var seats = showtime.Room!.Seats
            .Where(s => selection.SeatIds.Contains(s.Id))
            .OrderBy(s => s.RowNumber).ThenBy(s => s.ColumnNumber)
            .Select(s => new CheckoutSeatItem(
                s.Label, s.SeatType, _pricing.PriceFor(showtime.BasePrice, s.SeatType)))
            .ToList();

        var summary = new CheckoutViewModel(
            showtime.Id,
            showtime.Movie!.Title,
            showtime.Room.Theater!.Name,
            showtime.Room.Theater.City,
            showtime.Room.RoomName,
            _tz.ToLocalTime(showtime.StartTimeUtc),
            seats,
            seats.Sum(s => s.Price));

        return new CheckoutPageViewModel(summary, form);
    }

    private async Task<CheckoutFormModel> BuildCheckoutFormAsync(CancellationToken ct)
    {
        var form = new CheckoutFormModel();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return form;

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.Email })
            .FirstOrDefaultAsync(ct);

        if (user is null) return form;

        form.CustomerName = user.FullName ?? user.Email ?? string.Empty;
        form.CustomerEmail = user.Email ?? string.Empty;
        return form;
    }

    private Task<Booking?> FindBookingAsync(string code, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return _db.Bookings.AsNoTracking()
            .Include(b => b.Showtime!).ThenInclude(s => s.Movie)
            .Include(b => b.Showtime!).ThenInclude(s => s.Room!).ThenInclude(r => r.Theater)
            .Include(b => b.Seats).ThenInclude(bs => bs.Seat)
            .Include(b => b.Payment)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.BookingCode == normalized, ct);
    }

    private BookingTicketViewModel ToDetails(Booking booking)
    {
        var showtime = booking.Showtime!;

        var seats = booking.Seats
            .Where(bs => bs.Seat is not null)
            .OrderBy(bs => bs.Seat!.RowNumber).ThenBy(bs => bs.Seat!.ColumnNumber)
            .Select(bs => new BookingSeatLine(bs.Seat!.Label, bs.Seat.SeatType, bs.PriceAtBooking))
            .ToList();

        return new BookingTicketViewModel(
            booking.BookingCode,
            showtime.Movie?.Title ?? string.Empty,
            showtime.Room?.Theater?.Name ?? string.Empty,
            showtime.Room?.Theater?.City ?? string.Empty,
            showtime.Room?.RoomName ?? string.Empty,
            _tz.ToLocalTime(showtime.StartTimeUtc),
            _tz.ToLocalTime(booking.BookingTimeUtc),
            booking.Status,
            booking.Payment?.Status ?? PaymentStatus.Pending,
            booking.Payment?.PaymentMethod ?? "Mock",
            booking.Payment?.TransactionId,
            booking.CustomerName,
            booking.CustomerEmail,
            booking.CustomerPhone,
            booking.TotalAmount,
            seats);
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
