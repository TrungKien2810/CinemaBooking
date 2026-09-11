using System.Diagnostics;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Controllers;

public class HomeController : Controller
{
    private readonly CinemaDbContext _db;
    private readonly ILogger<HomeController> _logger;

    public HomeController(CinemaDbContext db, ILogger<HomeController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // AsNoTracking: trang chỉ đọc, không cần change tracker giữ lại entity.
        // Select trước ToListAsync để SQL chỉ kéo về đúng cột cần vẽ.
        var movies = await _db.Movies
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.ReleaseDate)
            .ThenBy(m => m.Title)
            .Select(m => new MovieCardViewModel
            {
                Id = m.Id,
                Title = m.Title,
                Genre = m.Genre,
                DurationMinutes = m.DurationMinutes,
                PosterUrl = m.PosterUrl,
                ReleaseDate = m.ReleaseDate,
            })
            .ToListAsync(ct);

        if (movies.Count == 0)
        {
            // Trang chủ trắng trơn gần như luôn là do chưa seed, không phải lỗi view.
            _logger.LogWarning("Không có phim nào đang hoạt động — kiểm tra seed catalog đã chạy chưa.");
        }

        return View(new HomeIndexViewModel { Movies = movies });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
