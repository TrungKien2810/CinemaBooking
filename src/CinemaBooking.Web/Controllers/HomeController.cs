using System.Diagnostics;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CinemaBooking.Web.Controllers;

public class HomeController : Controller
{
    private readonly IMovieCatalogService _catalog;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IMovieCatalogService catalog, ILogger<HomeController> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    /// <param name="q">Từ khóa tìm theo tên phim hoặc thể loại.</param>
    /// <param name="genre">Lọc đúng một thể loại.</param>
    public async Task<IActionResult> Index(string? q, string? genre, CancellationToken ct)
    {
        // Lọc chạy trong SQL ở service của Dev B (ADR 0001) — controller chỉ map sang view model.
        var query = new MovieSearchQuery(q, genre).Normalized();
        var movies = await _catalog.SearchAsync(query, ct);
        var genres = await _catalog.GetGenresAsync(ct);

        if (movies.Count == 0 && query == MovieSearchQuery.All)
        {
            // Trang chủ trắng trơn khi KHÔNG lọc gần như luôn là do chưa seed, không phải lỗi view.
            _logger.LogWarning("Không có phim nào đang hoạt động — kiểm tra seed catalog đã chạy chưa.");
        }

        return View(new HomeIndexViewModel(
            movies.Select(MovieCardViewModel.FromListItem).ToList(),
            query.Keyword, query.Genre, genres));
    }

    public IActionResult Privacy()
    {
        return View();
    }

    /// <param name="traceId">Do ExceptionHandlingMiddleware truyền sang khi chạy lại pipeline.</param>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(string? traceId)
    {
        return View(new ErrorViewModel
        {
            RequestId = traceId ?? Activity.Current?.Id ?? HttpContext.TraceIdentifier,
        });
    }
}
