using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Controllers;

/// <summary>Trang "Hệ thống rạp" phía khách: danh sách chi nhánh gom theo thành phố và trang chi tiết từng chi nhánh.</summary>
public class BranchesController : Controller
{
    private readonly IBranchDirectory _branches;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;

    public BranchesController(IBranchDirectory branches, IOptions<DisplayTimeZone> tz, TimeProvider clock)
    {
        _branches = branches;
        _tz = tz.Value;
        _clock = clock;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Service đã sắp theo thành phố rồi tên; GroupBy giữ nguyên thứ tự đó.
        var byCity = (await _branches.GetActiveAsync(ct))
            .GroupBy(b => b.City)
            .ToList();

        return View(byCity);
    }

    /// <summary>Phim, phòng chiếu và khuyến mãi của riêng một chi nhánh.</summary>
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var details = await _branches.GetDetailsAsync(id, nowUtc, _tz.ToLocalDate(nowUtc), ct);
        if (details is null) return NotFound();

        ViewData["Tz"] = _tz;
        return View(details);
    }
}
