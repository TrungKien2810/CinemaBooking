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

    /// <param name="q">Tìm theo tên chi nhánh, địa chỉ hoặc thành phố (không phân biệt dấu).</param>
    /// <param name="city">Lọc đúng một thành phố.</param>
    public async Task<IActionResult> Index(string? q, string? city, CancellationToken ct)
    {
        var all = await _branches.GetActiveAsync(ct);
        var cities = all.Select(b => b.City).Distinct().ToList();

        // city đến từ URL: chỉ nhận thành phố có thật.
        var selectedCity = cities.FirstOrDefault(c => string.Equals(c, city?.Trim(), StringComparison.OrdinalIgnoreCase));
        var keyword = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        // Service đã sắp theo thành phố rồi tên; GroupBy giữ nguyên thứ tự đó.
        var byCity = all
            .Where(b => selectedCity is null || b.City == selectedCity)
            .Where(b => TextSearch.Matches(keyword, b.Name, b.Address, b.City))
            .GroupBy(b => b.City)
            .ToList();

        return View(new BranchSearchViewModel(byCity, keyword, selectedCity, cities, all.Count));
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
