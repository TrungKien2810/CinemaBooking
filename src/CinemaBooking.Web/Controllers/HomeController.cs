using System.Diagnostics;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CinemaBooking.Web.Controllers;

public class HomeController : Controller
{
    private readonly IMovieCatalogService _catalog;
    private readonly ILogger<HomeController> _logger;
    private readonly TimeProvider _clock;
    private readonly DisplayTimeZone _tz;

    public HomeController(
        IMovieCatalogService catalog,
        ILogger<HomeController> logger,
        TimeProvider? clock = null,
        Microsoft.Extensions.Options.IOptions<DisplayTimeZone>? tz = null)
    {
        _clock = clock ?? TimeProvider.System;
        _tz = tz?.Value ?? new DisplayTimeZone();
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

        // Gợi ý luôn lấy từ TOÀN BỘ phim, kể cả khi đang lọc (nếu không thì gõ xong một từ là hết gợi ý khác).
        var everything = query == MovieSearchQuery.All ? movies : await _catalog.SearchAsync(MovieSearchQuery.All, ct);
        var suggestions = everything.Select(m => m.Title).Concat(genres)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).ToList();

        return View(new HomeIndexViewModel(
            movies.Select(MovieCardViewModel.FromListItem).ToList(),
            query.Keyword, query.Genre, genres,
            Today: _tz.ToLocalDate(_clock.GetUtcNow().UtcDateTime),
            Suggestions: suggestions));
    }

    public IActionResult Privacy()
    {
        return View();
    }

    /// <summary>Trang lỗi cho các mã trạng thái không có nội dung (chạy lại từ UseStatusCodePages).</summary>
    [Route("Home/Status/{id:int}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int id) => View("Status", id);

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
