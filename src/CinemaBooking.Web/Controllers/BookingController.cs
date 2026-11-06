using System.Security.Claims;
using CinemaBooking.Domain;
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
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly IQrCodeService _qr;
    private readonly IVnpayPaymentService _vnpay;
    private readonly IHubContext<SeatHub, ISeatClient> _seats;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;
    private readonly SeatHoldOptions _hold;
    private readonly IOptions<VnpayOptions> _vnpayOptions;
    private readonly IBranchScope _branches;
    private readonly IVoucherService _vouchers;

    public BookingController(
        CinemaDbContext db,
        IBookedSeatQuery bookedSeats,
        ISeatLockManager locks,
        ISeatPricingService pricing,
        IBookingService bookings,
        IQrCodeService qr,
        IVnpayPaymentService vnpay,
        IHubContext<SeatHub, ISeatClient> seats,
        IOptions<DisplayTimeZone> tz,
        TimeProvider clock,
        IOptions<SeatHoldOptions> hold,
        IOptions<VnpayOptions> vnpayOptions,
        IBranchScope branches,
        IVoucherService vouchers)
    {
        _branches = branches;
        _vouchers = vouchers;
        _db = db;
        _bookedSeats = bookedSeats;
        _locks = locks;
        _pricing = pricing;
        _bookings = bookings;
        _qr = qr;
        _vnpay = vnpay;
        _seats = seats;
        _tz = tz.Value;
        _clock = clock;
        _hold = hold.Value;
        _vnpayOptions = vnpayOptions;
    }

    private const int MyBookingsPageSize = 10;

    /// <summary>Tài khoản quản trị (Admin/BranchAdmin) chỉ XEM sơ đồ ghế, không đặt vé — mua vé là việc của khách.</summary>
    private bool IsStaff => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.BranchAdmin);

    private IActionResult StaffCannotBuy(int? showtimeId = null)
    {
        TempData["Error"] = "Tài khoản quản trị không đặt vé. Hãy dùng tài khoản khách để mua vé.";
        return showtimeId is { } id ? RedirectToAction(nameof(SeatMap), new { id }) : RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public async Task<IActionResult> SeatMap(int id, CancellationToken ct)
    {
        var showtime = await FindBookableShowtimeAsync(id, ct);
        if (showtime is null) return NotFound();
        ViewData["StaffPreview"] = IsStaff;

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
        var startLocal = _tz.ToLocalTime(showtime.StartTimeUtc);
        var promotions = await PromotionRules.LoadForShowtimeAsync(
            _db, showtime.Room!.TheaterId, DateOnly.FromDateTime(startLocal), ct);
        var surcharges = await PricingLoader.LoadSurchargesAsync(_db, showtime.Room.TheaterId, ct);

        return View(SeatMapViewModel.Build(
            showtime, booked, _pricing, startLocal, (int)_hold.Duration.TotalSeconds, promotions, surcharges));
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
        if (IsStaff) return StaffCannotBuy(showtimeId);

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

        // Đổi sang suất khác khi đang có ghế chờ thanh toán: giỏ chỉ giữ một suất, nên trả ghế cũ
        // ngay thay vì để chúng khóa người khác cho tới khi hết hạn.
        if (HttpContext.Session.GetSeatSelection() is { } previous && previous.ShowtimeId != showtimeId)
        {
            var released = previous.SeatIds
                .Where(seatId => _locks.Release(new SeatKey(previous.ShowtimeId, seatId), previous.ConnectionId))
                .ToList();
            if (released.Count > 0)
                await _seats.Clients.Group(SeatHub.GroupName(previous.ShowtimeId)).SeatUnlocked(released);
        }

        // Mốc hết hạn để trang thanh toán chạy đồng hồ đếm ngược — cùng một con số với lock.
        var expiresUtc = _clock.GetUtcNow().UtcDateTime.Add(_hold.Duration);
        HttpContext.Session.SetSeatSelection(new SeatSelection(showtimeId, ids, holdToken, expiresUtc));
        return RedirectToAction(nameof(Checkout));
    }

    /// <summary>
    /// Tóm tắt giỏ ghế trong Session + form liên hệ. Khách đã đăng nhập được điền sẵn
    /// họ tên/email để không phải gõ lại.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Checkout(CancellationToken ct)
    {
        if (IsStaff) return StaffCannotBuy();

        var selection = HttpContext.Session.GetSeatSelection();
        if (selection is null) return RedirectToAction("Index", "Home");

        var page = await BuildCheckoutPageAsync(selection, await BuildCheckoutFormAsync(ct), ct);
        if (page is null) return RedirectToAction("Index", "Home");

        // Hết hạn giữ ghế thì không cho điền form nữa: đưa về sơ đồ ghế kèm thông báo, tránh
        // cảnh khách gõ xong mới bị báo "ghế đã hết hạn".
        if (page.HoldSecondsRemaining <= 0)
        {
            HttpContext.Session.ClearSeatSelection();
            TempData["Error"] = "Đã hết thời gian giữ ghế. Vui lòng chọn lại.";
            return RedirectToAction(nameof(SeatMap), new { id = selection.ShowtimeId });
        }

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
        if (IsStaff) return StaffCannotBuy();

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
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            HttpContext.Session.GetVoucherCode()), ct);

        if (result.IsRedirectToPayment)
        {
            // Luồng VNPAY: Booking + Payment đang ở trạng thái Pending (BookingSeats giữ ghế), khách
            // cần được chuyển hướng sang cổng VNPAY để thanh toán. Quay về sau khi VNPAY xử lý
            // (qua ReturnUrl / IPN) sẽ gọi lại để xác nhận hoặc hủy đơn.
            return Redirect(result.PaymentUrl ?? string.Empty);
        }

        if (!result.IsSuccess)
        {
            // Mã giảm giá không còn dùng được (hết lượt, hết hạn...): bỏ mã để khách thanh toán tiếp giá thường hoặc nhập mã khác.
            if (result.Kind == BookingResultKind.VoucherInvalid) HttpContext.Session.ClearVoucherCode();
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Checkout));
        }

        // Ghế đã được đặt: báo cho mọi máy đang mở suất này vẽ lại thành "đã đặt" (General.md §6.2 bước 5).
        await _seats.Clients.Group(SeatHub.GroupName(selection.ShowtimeId)).SeatBooked(selection.SeatIds.ToList());

        HttpContext.Session.ClearSeatSelection();
        // Khách guest vừa đặt: nhớ mã vé trong phiên để trang xác nhận hiện thông tin đầy đủ của chính họ.
        if (result.BookingCode is not null) HttpContext.Session.RememberOwnedBooking(result.BookingCode);
        return RedirectToAction(nameof(Confirmation), new { code = result.BookingCode });
    }

    /// <summary>
    /// Khách nhập mã giảm giá ở trang thanh toán. Chỉ LƯU mã vào Session; việc kiểm tra (hạn, chi nhánh, đơn tối thiểu, lượt dùng)
    /// diễn ra khi dựng lại trang thanh toán và lần nữa ở server lúc chốt đơn.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ApplyVoucher(string? code)
    {
        if (IsStaff) return StaffCannotBuy();
        if (HttpContext.Session.GetSeatSelection() is null) return RedirectToAction("Index", "Home");

        var normalized = VoucherRules.Normalize(code);
        if (normalized.Length == 0)
        {
            TempData["VoucherError"] = "Nhập mã giảm giá.";
        }
        else if (normalized.Length > 30)
        {
            TempData["VoucherError"] = "Mã giảm giá không hợp lệ.";
        }
        else
        {
            HttpContext.Session.SetVoucherCode(normalized);
        }

        return RedirectToAction(nameof(Checkout));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveVoucher()
    {
        HttpContext.Session.ClearVoucherCode();
        return RedirectToAction(nameof(Checkout));
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
            TxnRef: response.GetValueOrDefault("vnp_TxnRef") ?? string.Empty,
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
            // Phiên này là phiên đã khởi tạo thanh toán — được xem đầy đủ thông tin vé của mình.
            if (result.IsSuccess && result.BookingCode is not null)
                HttpContext.Session.RememberOwnedBooking(result.BookingCode);

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
            TxnRef: response.GetValueOrDefault("vnp_TxnRef") ?? string.Empty,
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

        return View(ToDetails(booking, await CanSeePersonalDataAsync(booking, ct)));
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
    [EnableRateLimiting(TicketRateLimit.Policy)]
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

        return View(new BookingLookupViewModel(
            booking.BookingCode, null, ToDetails(booking, await CanSeePersonalDataAsync(booking, ct))));
    }

    /// <summary>Vé của tài khoản đang đăng nhập, mới nhất trước.</summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> MyBookings(string? tab, int? page, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();
        if (IsStaff) return StaffCannotBuy();

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
        // Suất đang chiếu vẫn là "sắp chiếu" với khách: chỉ khi hết phim mới sang "đã xem".
        var upcomingCount = bookings.Count(b =>
            (b.Status is BookingStatus.Confirmed or BookingStatus.Pending) && b.Showtime!.EndTimeUtc > now);
        var historyCount = bookings.Count(b =>
            b.Status == BookingStatus.Confirmed && b.Showtime!.EndTimeUtc <= now);

        var selected = string.IsNullOrWhiteSpace(tab) ? "all" : tab.Trim().ToLowerInvariant();
        var filtered = selected switch
        {
            "upcoming" => bookings.Where(b =>
                (b.Status is BookingStatus.Confirmed or BookingStatus.Pending) && b.Showtime!.EndTimeUtc > now),
            "history" => bookings.Where(b =>
                b.Status == BookingStatus.Confirmed && b.Showtime!.EndTimeUtc <= now),
            _ => bookings,
        };

        var matching = filtered.ToList();
        var current = PageInfo.Clamp(page, matching.Count, MyBookingsPageSize);

        // Vé của chính tài khoản đang đăng nhập (đã lọc theo UserId ở trên) — hiện đầy đủ.
        var items = matching
            .Skip((current - 1) * MyBookingsPageSize)
            .Take(MyBookingsPageSize)
            .Select(b => ToDetails(b, revealPersonalData: true))
            .ToList();

        return View(new MyBookingsViewModel(
            items, selected, upcomingCount, historyCount, bookings.Count,
            new PageInfo(current, MyBookingsPageSize, matching.Count)));
    }

    /// <summary>Gửi lại email xác nhận; khách guest phải nhập đúng email trên vé.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(TicketRateLimit.Policy)]
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

        var startLocal = _tz.ToLocalTime(showtime.StartTimeUtc);
        var promotions = await PromotionRules.LoadForShowtimeAsync(
            _db, showtime.Room!.TheaterId, DateOnly.FromDateTime(startLocal), ct);
        var surcharges = await PricingLoader.LoadSurchargesAsync(_db, showtime.Room!.TheaterId, ct);
        var seats = BuildCheckoutSeats(showtime, selection.SeatIds, startLocal, promotions, surcharges);

        // Mã giảm giá đang áp (lưu ở Session): kiểm lại mỗi lần dựng trang — mã có thể vừa hết lượt/hết hạn.
        var subtotal = seats.Sum(s => s.Price);
        string? voucherCode = null, voucherTitle = null;
        var voucherDiscount = 0m;
        if (HttpContext.Session.GetVoucherCode() is { Length: > 0 } stored)
        {
            var check = await _vouchers.CheckAsync(
                stored, showtime.Room.TheaterId, subtotal, User.FindFirstValue(ClaimTypes.NameIdentifier), User.Identity?.Name, ct);
            if (check.IsOk)
            {
                voucherCode = check.Voucher!.Code;
                voucherTitle = check.Voucher.Title;
                voucherDiscount = check.Discount;
            }
            else
            {
                HttpContext.Session.ClearVoucherCode();
                TempData["VoucherError"] = check.Message;
            }
        }

        var summary = new CheckoutViewModel(
            showtime.Id,
            showtime.Movie!.Title,
            showtime.Room!.Theater!.Name,
            showtime.Room.Theater.City,
            showtime.Room.RoomName,
            _tz.ToLocalTime(showtime.StartTimeUtc),
            seats,
            subtotal - voucherDiscount,
            showtime.Room.RoomType.Label() + " · " + showtime.Language.Label(),
            voucherCode, voucherDiscount, voucherTitle);

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var remaining = selection.ExpiresUtc is { } expiresUtc
            ? (int)Math.Max(0, (expiresUtc - nowUtc).TotalSeconds)
            : (int)_hold.Duration.TotalSeconds;

        return new CheckoutPageViewModel(summary, form, remaining);
    }

    /// <summary>
    /// Một dòng cho mỗi ghế, riêng ghế đôi gộp cả cặp thành một dòng "J1–J2" với giá của cả cặp —
    /// khớp với khối ghế trên sơ đồ, không để khách thấy hai dòng nửa ghế.
    /// </summary>
    private List<CheckoutSeatItem> BuildCheckoutSeats(
        Showtime showtime, IReadOnlyCollection<int> selectedIds, DateTime startLocal, IReadOnlyList<Promotion> promotions,
        SeatSurcharges? surcharges)
    {
        var allSeats = showtime.Room!.Seats.ToList();
        var byId = allSeats.ToDictionary(s => s.Id);
        var partners = SeatPairing.BuildPartnerMap(allSeats);
        var selected = selectedIds.ToHashSet();

        var items = new List<CheckoutSeatItem>();
        var consumed = new HashSet<int>();

        foreach (var seat in allSeats
                     .Where(s => selected.Contains(s.Id))
                     .OrderBy(s => s.RowNumber).ThenBy(s => s.ColumnNumber))
        {
            if (!consumed.Add(seat.Id)) continue;

            var unit = PromotionRules.Quote(
                _pricing.PriceFor(showtime.BasePrice, seat.SeatType, surcharges), seat.SeatType, startLocal, promotions);

            if (seat.SeatType == SeatType.Couple
                && partners.TryGetValue(seat.Id, out var partnerId)
                && selected.Contains(partnerId))
            {
                consumed.Add(partnerId);
                var partner = byId[partnerId];
                items.Add(new CheckoutSeatItem(
                    $"{seat.Label}–{partner.Label}", SeatType.Couple, unit.FinalPrice * 2, unit.ListPrice * 2, unit.PromotionTitle));
                continue;
            }

            items.Add(new CheckoutSeatItem(seat.Label, seat.SeatType, unit.FinalPrice, unit.ListPrice, unit.PromotionTitle));
        }

        return items;
    }

    private async Task<CheckoutFormModel> BuildCheckoutFormAsync(CancellationToken ct)
    {
        var form = new CheckoutFormModel();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return form;

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.Email, u.PhoneNumber })
            .FirstOrDefaultAsync(ct);

        if (user is null) return form;

        form.CustomerName = user.FullName ?? user.Email ?? string.Empty;
        form.CustomerEmail = user.Email ?? string.Empty;
        form.CustomerPhone = user.PhoneNumber;
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

    private BookingTicketViewModel ToDetails(Booking booking, bool revealPersonalData) =>
        BookingTicketMapper.ToViewModel(booking, _tz, revealPersonalData);

    /// <summary>
    /// Ai được xem họ tên/email/SĐT đầy đủ trên trang vé: chủ tài khoản đặt vé, khách vừa đặt trong phiên
    /// này (guest), Admin, và BranchAdmin của đúng chi nhánh chiếu. Người chỉ có mã vé chỉ thấy bản đã che.
    /// </summary>
    private async Task<bool> CanSeePersonalDataAsync(Booking booking, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null && string.Equals(booking.UserId, userId, StringComparison.Ordinal)) return true;
        if (HttpContext.Session.OwnsBooking(booking.BookingCode)) return true;

        var theaterId = booking.Showtime?.Room?.TheaterId;
        return theaterId is not null && await _branches.CanAccessTheaterAsync(theaterId.Value, ct);
    }

    private async Task<string?> ValidateSelectionAsync(Showtime showtime, List<int> ids, string? connectionId, CancellationToken ct)
    {
        if (ids.Count == 0) return "Chưa chọn ghế nào.";
        if (string.IsNullOrWhiteSpace(connectionId)) return "Mất kết nối giữ ghế, vui lòng tải lại trang.";

        var inRoom = showtime.Room!.Seats.Count(s => ids.Contains(s.Id));
        if (inRoom != ids.Count) return "Có ghế không thuộc phòng chiếu của suất này.";

        // Ghế đôi phải đi theo cặp — kiểm tra trước khi chuyển lock sang hold-token.
        var pairingError = SeatPairing.Validate(showtime.Room!.Seats, ids);
        if (pairingError is not null) return pairingError;

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
                                      && s.IsActive && s.Movie!.IsActive && s.Room!.Theater!.IsActive, ct);
    }
}
