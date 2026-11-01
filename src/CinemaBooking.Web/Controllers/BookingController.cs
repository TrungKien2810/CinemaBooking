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
    private readonly IBookingCancellationService _cancellations;
    private readonly IQrCodeService _qr;
    private readonly IVnpayPaymentService _vnpay;
    private readonly IHubContext<SeatHub, ISeatClient> _seats;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;
    private readonly SeatHoldOptions _hold;
    private readonly IOptions<VnpayOptions> _vnpayOptions;

    public BookingController(
        CinemaDbContext db,
        IBookedSeatQuery bookedSeats,
        ISeatLockManager locks,
        ISeatPricingService pricing,
        IBookingService bookings,
        IBookingCancellationService cancellations,
        IQrCodeService qr,
        IVnpayPaymentService vnpay,
        IHubContext<SeatHub, ISeatClient> seats,
        IOptions<DisplayTimeZone> tz,
        TimeProvider clock,
        IOptions<SeatHoldOptions> hold,
        IOptions<VnpayOptions> vnpayOptions)
    {
        _db = db;
        _bookedSeats = bookedSeats;
        _locks = locks;
        _pricing = pricing;
        _bookings = bookings;
        _cancellations = cancellations;
        _qr = qr;
        _vnpay = vnpay;
        _seats = seats;
        _tz = tz.Value;
        _clock = clock;
        _hold = hold.Value;
        _vnpayOptions = vnpayOptions;
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

        if (result.IsRedirectToPayment)
        {
            // Luồng VNPAY: Booking + Payment đang ở trạng thái Pending (BookingSeats giữ ghế), khách
            // cần được chuyển hướng sang cổng VNPAY để thanh toán. Quay về sau khi VNPAY xử lý
            // (qua ReturnUrl / IPN) sẽ gọi lại để xác nhận hoặc hủy đơn.
            return Redirect(result.PaymentUrl ?? string.Empty);
        }

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

    /// <summary>
    /// URL nhận kết quả sau khi khách hàng hoàn tất thanh toán online tại cổng VNPAY (<c>vnp_ReturnUrl</c>).
    /// Xác minh chữ ký <c>vnp_SecureHash</c>, xác nhận / hủy đơn, rồi đưa sang trang xác nhận vé.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> PaymentReturn(CancellationToken ct)
    {
        var response = new Dictionary<string, string?>();
        foreach (var key in Request.Query.Keys)
        {
            if (!key.StartsWith("vnp_", StringComparison.OrdinalIgnoreCase)) continue;
            response[key] = Request.Query[key];
        }

        var vnpayRequest = new VnpayPaymentRequest(
            TxnRef: response["vnp_TxnRef"] ?? string.Empty,
            Amount: 0,
            OrderInfo: string.Empty,
            ReturnUrl: _vnpayOptions.Value.ReturnUrl,
            TmnCode: _vnpayOptions.Value.TmnCode,
            HashSecret: _vnpayOptions.Value.HashSecret,
            IpAddr: _vnpayOptions.Value.IpAddr,
            Locale: _vnpayOptions.Value.Locale,
            OrderType: _vnpayOptions.Value.OrderType,
            ExpireMinutes: _vnpayOptions.Value.ExpireMinutes);

        var verify = await _vnpay.VerifyVnpayPaymentAsync(vnpayRequest, response, ct);

        // Chữ ký sai: không đổi trạng thái đơn, cũng không báo ghế trống cho các tab khác.
        if (!verify.SignatureValid)
        {
            TempData["Error"] = "Không xác thực được phản hồi từ VNPAY (chữ ký không hợp lệ). Vé chưa được xác nhận.";
            return RedirectToAction(nameof(Confirmation), new { code = verify.TxnRef });
        }

        var result = await _bookings.ConfirmAsync(verify.TxnRef ?? string.Empty, verify, ct);

        // Đơn đã có kết quả: giỏ ghế trong Session hết tác dụng. Báo cho các tab đang mở suất chiếu
        // vẽ lại ghế ngay — thanh toán xong thì "đã đặt", thanh toán lỗi/hủy thì "trống" (ghế đã được
        // trả trong ConfirmAsync), không phải chờ lock hết hạn.
        var selection = HttpContext.Session.GetSeatSelection();
        HttpContext.Session.ClearSeatSelection();
        if (selection is not null)
        {
            var seatIds = selection.SeatIds.ToList();
            if (result.IsSuccess)
                await _seats.Clients.Group(SeatHub.GroupName(selection.ShowtimeId)).SeatBooked(seatIds);
            else
                await _seats.Clients.Group(SeatHub.GroupName(selection.ShowtimeId)).SeatUnlocked(seatIds);
        }

        if (!result.IsSuccess)
            TempData["Error"] = result.Message;

        return RedirectToAction(nameof(Confirmation), new { code = result.BookingCode ?? verify.TxnRef });
    }

    /// <summary>
    /// IPN URL của VNPAY: cổng thanh toán gọi lại máy chủ để notify kết quả sau khi khách thanh toán.
    /// Bắt buộc trả JSON <c>{"RspCode":"00","Message":"..."}</c> cho VNPAY. Sau đó xác nhận / hủy
    /// đơn đặt vé tương ứng.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> PaymentIpn(CancellationToken ct)
    {
        var response = new Dictionary<string, string?>();
        foreach (var key in Request.Query.Keys)
        {
            if (!key.StartsWith("vnp_", StringComparison.OrdinalIgnoreCase)) continue;
            response[key] = Request.Query[key];
        }

        var vnpayRequest = new VnpayPaymentRequest(
            TxnRef: response["vnp_TxnRef"] ?? string.Empty,
            Amount: 0,
            OrderInfo: string.Empty,
            ReturnUrl: _vnpayOptions.Value.ReturnUrl,
            TmnCode: _vnpayOptions.Value.TmnCode,
            HashSecret: _vnpayOptions.Value.HashSecret,
            IpAddr: _vnpayOptions.Value.IpAddr,
            Locale: _vnpayOptions.Value.Locale,
            OrderType: _vnpayOptions.Value.OrderType,
            ExpireMinutes: _vnpayOptions.Value.ExpireMinutes);

        var verify = await _vnpay.VerifyVnpayPaymentAsync(vnpayRequest, response, ct);
        await _bookings.ConfirmAsync(verify.TxnRef ?? string.Empty, verify, ct);

        var responseCode = verify.SignatureValid ? "00" : "97";
        var message = verify.SignatureValid ? "Confirm Success" : "Invalid signature";

        return Content("{\"RspCode\":\"" + responseCode + "\",\"Message\":\"" + message + "\"}", "application/json");
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

        var eligibility = await _cancellations.GetEligibilityAsync(booking, ct);
        return View(ToDetails(booking, eligibility.CanCancel, eligibility.RefundAmount));
    }

    /// <summary>
    /// Ảnh QR in trên vé. Nội dung là URL tuyệt đối tới trang xác nhận để nhân viên quét bằng
    /// điện thoại; URL dùng host hiện tại nên chạy đúng cả localhost và tên miền thật.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Qr(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return NotFound();

        var normalized = code.Trim().ToUpperInvariant();
        var exists = await _db.Bookings.AsNoTracking()
            .AnyAsync(b => b.BookingCode == normalized, ct);
        if (!exists) return NotFound();

        var url = Url.Action(nameof(Confirmation), "Booking", new { code = normalized }, Request.Scheme);
        if (string.IsNullOrWhiteSpace(url)) return NotFound();

        return File(_qr.CreatePng(url), "image/png");
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

        var eligibility = await _cancellations.GetEligibilityAsync(booking, ct);
        return View(new BookingLookupViewModel(
            booking.BookingCode, null, ToDetails(booking, eligibility.CanCancel, eligibility.RefundAmount)));
    }

    /// <summary>Vé của tài khoản đang đăng nhập, mới nhất trước.</summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> MyBookings(string? tab, CancellationToken ct)
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

        var now = _clock.GetUtcNow().UtcDateTime;
        var upcomingCount = bookings.Count(b =>
            (b.Status is BookingStatus.Confirmed or BookingStatus.Pending) && b.Showtime!.StartTimeUtc > now);
        var historyCount = bookings.Count(b =>
            b.Status == BookingStatus.Confirmed && b.Showtime!.StartTimeUtc <= now);
        var cancelledCount = bookings.Count(b =>
            b.Status is BookingStatus.Cancelled or BookingStatus.Expired);

        var selected = string.IsNullOrWhiteSpace(tab) ? "all" : tab.Trim().ToLowerInvariant();
        var filtered = selected switch
        {
            "upcoming" => bookings.Where(b =>
                (b.Status is BookingStatus.Confirmed or BookingStatus.Pending) && b.Showtime!.StartTimeUtc > now),
            "history" => bookings.Where(b =>
                b.Status == BookingStatus.Confirmed && b.Showtime!.StartTimeUtc <= now),
            "cancelled" => bookings.Where(b =>
                b.Status is BookingStatus.Cancelled or BookingStatus.Expired),
            _ => bookings,
        };

        var items = new List<BookingTicketViewModel>();
        foreach (var booking in filtered)
        {
            var eligibility = await _cancellations.GetEligibilityAsync(booking, ct);
            items.Add(ToDetails(booking, eligibility.CanCancel, eligibility.RefundAmount));
        }

        return View(new MyBookingsViewModel(
            items, selected, upcomingCount, historyCount, cancelledCount));
    }

    /// <summary>
    /// Trang xác nhận hủy vé. Khách đã đăng nhập chỉ xem được vé của mình; khách guest phải
    /// nhập lại email khi bấm hủy ở bước POST.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Cancel(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return View("BookingNotFound", (string?)null);

        var booking = await FindBookingAsync(code, ct);
        if (booking is null)
            return View("BookingNotFound", code.Trim());

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (User.Identity?.IsAuthenticated == true)
        {
            var email = User.Identity.Name;
            var owns = string.Equals(booking.UserId, userId, StringComparison.Ordinal)
                       || (booking.UserId is null
                           && string.Equals(booking.CustomerEmail, email, StringComparison.OrdinalIgnoreCase));
            if (!owns) return Forbid();
        }
        else if (booking.UserId is not null)
        {
            // Vé đã gắn tài khoản: bắt đăng nhập rồi quay lại trang hủy.
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Cancel), new { code }) });
        }

        var eligibility = await _cancellations.GetEligibilityAsync(booking, ct);
        var details = ToDetails(booking, eligibility.CanCancel, eligibility.RefundAmount);

        return View(new CancelBookingPageViewModel(
            details,
            eligibility.CanCancel,
            eligibility.Reason,
            eligibility.RefundAmount,
            RequiresEmail: User.Identity?.IsAuthenticated != true));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(string code, string? email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return View("BookingNotFound", (string?)null);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.IsInRole("Admin");
        var authEmail = User.Identity?.IsAuthenticated == true ? User.Identity.Name : email?.Trim();
        var actor = isAdmin
            ? $"admin:{userId}"
            : userId is not null
                ? $"customer:{userId}"
                : $"guest:{authEmail}";

        var result = await _cancellations.CancelAsync(
            new CancelBookingRequest(code, userId, authEmail, isAdmin, actor), ct);

        if (result.IsSuccess)
        {
            if (result.ShowtimeId > 0 && result.SeatIds is { Count: > 0 })
            {
                await _seats.Clients.Group(SeatHub.GroupName(result.ShowtimeId))
                    .SeatUnlocked(result.SeatIds);
            }

            TempData["Success"] = result.Message;
            return User.Identity?.IsAuthenticated == true
                ? RedirectToAction(nameof(MyBookings))
                : RedirectToAction(nameof(Confirmation), new { code });
        }

        TempData["Error"] = result.Message;
        if (result.Outcome == CancelBookingOutcome.NotFound)
            return View("BookingNotFound", code);

        return RedirectToAction(nameof(Cancel), new { code });
    }

    /// <summary>Gửi lại email xác nhận; khách guest phải nhập đúng email trên vé.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendEmail(string code, string? email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return View("BookingNotFound", (string?)null);

        var booking = await FindBookingAsync(code, ct);
        if (booking is null) return View("BookingNotFound", code);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var authEmail = User.Identity?.IsAuthenticated == true ? User.Identity.Name : email?.Trim();
        var owns = (!string.IsNullOrWhiteSpace(userId)
                    && string.Equals(booking.UserId, userId, StringComparison.Ordinal))
                   || (booking.UserId is null
                       && !string.IsNullOrWhiteSpace(authEmail)
                       && string.Equals(booking.CustomerEmail, authEmail, StringComparison.OrdinalIgnoreCase));

        if (!owns)
        {
            TempData["Error"] = "Email không khớp với vé. Vui lòng kiểm tra lại.";
            return RedirectToAction(nameof(Confirmation), new { code = booking.BookingCode });
        }

        var result = await _bookings.ResendConfirmationAsync(booking.BookingCode, ct);
        if (result.IsSuccess)
            TempData["Success"] = $"Đã xếp email xác nhận gửi lại tới {booking.CustomerEmail}.";
        else
            TempData["Error"] = result.Message;

        return RedirectToAction(nameof(Confirmation), new { code = booking.BookingCode });
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

    private BookingTicketViewModel ToDetails(Booking booking, bool canCancel = false, decimal refundAmount = 0)
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
            booking.Payment?.PaymentMethod ?? "VNPAY",
            booking.Payment?.TransactionId,
            booking.CustomerName,
            booking.CustomerEmail,
            booking.CustomerPhone,
            booking.TotalAmount,
            seats,
            canCancel,
            refundAmount);
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
