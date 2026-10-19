using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CinemaBooking.Web.Controllers;

/// <summary>Trang "Hệ thống rạp" phía khách: chi nhánh đang mở, gom theo thành phố.</summary>
public class BranchesController : Controller
{
    private readonly IBranchDirectory _branches;

    public BranchesController(IBranchDirectory branches) => _branches = branches;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Service đã sắp theo thành phố rồi tên; GroupBy giữ nguyên thứ tự đó.
        var byCity = (await _branches.GetActiveAsync(ct))
            .GroupBy(b => b.City)
            .ToList();

        return View(byCity);
    }
}
