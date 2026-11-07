using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>
/// Soát vé tại rạp: nhân viên nhập (hoặc quét QR rồi bấm "Soát vé này" từ trang vé) mã vé. Một vé chỉ vào
/// được một lần; BranchAdmin chỉ soát được vé của suất chiếu thuộc chi nhánh mình.
/// </summary>
public class CheckInController : AdminControllerBase
{
    private readonly ITicketCheckInService _checkIn;
    private readonly IBranchScope _branches;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;

    public CheckInController(
        ITicketCheckInService checkIn, IBranchScope branches, IOptions<DisplayTimeZone> tz, TimeProvider clock)
    {
        _checkIn = checkIn;
        _branches = branches;
        _tz = tz.Value;
        _clock = clock;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return View(new CheckInPageModel(null, null, null, null, null));

        return View(await BuildAsync(code, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return RedirectToAction(nameof(Index));

        var booking = await _checkIn.FindAsync(code, ct);
        if (booking is null || !await _branches.CanAccessTheaterAsync(booking.Showtime!.Room!.TheaterId, ct))
        {
            // Cùng một thông báo cho "không có" và "của chi nhánh khác" — không để dò mã vé.
            SetError("Không tìm thấy vé với mã này tại chi nhánh của bạn.");
            return RedirectToAction(nameof(Index), new { code });
        }

        var staff = User.Identity?.Name ?? "unknown";
        var outcome = await _checkIn.CheckInAsync(booking.BookingCode, staff, ct);

        if (outcome == CheckInOutcome.Ok)
        {
            if (TempData is not null) TempData["Message"] = $"Đã soát vé {booking.BookingCode} — cho khách vào rạp.";
        }
        else
        {
            SetError(Describe(outcome));
        }

        return RedirectToAction(nameof(Index), new { code = booking.BookingCode });
    }

    private async Task<CheckInPageModel> BuildAsync(string code, CancellationToken ct)
    {
        var booking = await _checkIn.FindAsync(code, ct);
        if (booking is null) return new CheckInPageModel(code.Trim(), CheckInOutcome.NotFound, null, null, null);

        var theater = booking.Showtime!.Room!.Theater!;
        // Vé của chi nhánh khác: trả đúng kết quả như "không tìm thấy" — không cho BranchAdmin dò mã vé
        // hay biết mã đó thuộc rạp nào.
        if (!await _branches.CanAccessTheaterAsync(theater.Id, ct))
            return new CheckInPageModel(code.Trim(), CheckInOutcome.NotFound, null, null, null);

        var outcome = TicketCheckInRules.Evaluate(booking, _clock.GetUtcNow().UtcDateTime);
        var opens = _tz.ToLocalTime(booking.Showtime.StartTimeUtc - TicketCheckInRules.OpensBefore);

        // Nhân viên cần thấy đủ tên/ghế để đối chiếu với khách trước mặt.
        return new CheckInPageModel(
            booking.BookingCode, outcome, null,
            BookingTicketMapper.ToViewModel(booking, _tz, revealPersonalData: true), opens);
    }

    public static string Describe(CheckInOutcome outcome) => outcome switch
    {
        CheckInOutcome.Ok => "Vé hợp lệ.",
        CheckInOutcome.NotFound => "Không tìm thấy vé với mã này.",
        CheckInOutcome.NotPaid => "Vé chưa thanh toán, đã hủy hoặc đã hết hạn — không cho vào.",
        CheckInOutcome.TooEarly => "Chưa tới giờ soát vé của suất này.",
        CheckInOutcome.ShowEnded => "Suất chiếu đã kết thúc.",
        CheckInOutcome.AlreadyCheckedIn => "Vé này đã được soát rồi — không cho vào lần hai.",
        _ => outcome.ToString(),
    };

    private void SetError(string message)
    {
        if (TempData is not null) TempData["Error"] = message;
    }
}
