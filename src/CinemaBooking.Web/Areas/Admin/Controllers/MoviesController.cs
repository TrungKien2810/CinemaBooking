using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class MoviesController : AdminControllerBase
{
    private readonly CinemaDbContext _db;

    public MoviesController(CinemaDbContext db) => _db = db;

    /// <summary>
    /// Liệt kê cả phim đã ẩn — admin cần thấy để bật lại. Tìm theo tên/thể loại/đạo diễn/diễn viên (không phân biệt
    /// dấu), lọc theo trạng thái ("active", "hidden", "soon" = chưa khởi chiếu) và thể loại.
    /// </summary>
    public async Task<IActionResult> Index(string? q, string? status, string? genre, CancellationToken ct)
    {
        var all = await _db.Movies.AsNoTracking()
            .OrderByDescending(m => m.IsActive)
            .ThenByDescending(m => m.ReleaseDate)
            .ThenBy(m => m.Title)
            .ToListAsync(ct);

        var genres = all.Select(m => m.Genre).Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g).ToList();
        var selectedGenre = genres.FirstOrDefault(g => string.Equals(g, genre?.Trim(), StringComparison.OrdinalIgnoreCase));
        var keyword = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        var statusFilter = status is "active" or "hidden" or "soon" ? status : null;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

        var items = all
            .Where(m => selectedGenre is null || string.Equals(m.Genre?.Trim(), selectedGenre, StringComparison.OrdinalIgnoreCase))
            .Where(m => statusFilter switch
            {
                "active" => m.IsActive && m.ReleaseDate <= today,
                "hidden" => !m.IsActive,
                "soon" => m.IsActive && m.ReleaseDate > today,
                _ => true,
            })
            .Where(m => TextSearch.Matches(keyword, m.Title, m.Genre, m.Director, m.Cast))
            .ToList();

        return View(new MoviesIndexViewModel(items, keyword, statusFilter, selectedGenre, genres, all.Count, all.Count(m => m.IsActive)));
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new MovieFormModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MovieFormModel form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Form", form);

        var movie = new Movie();
        form.ApplyTo(movie);
        _db.Movies.Add(movie);
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var movie = await _db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        return movie is null ? NotFound() : View("Form", MovieFormModel.From(movie));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, MovieFormModel form, CancellationToken ct)
    {
        var movie = await _db.Movies.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (movie is null) return NotFound();

        form.Id = id;
        if (!ModelState.IsValid) return View("Form", form);

        form.ApplyTo(movie);
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Soft delete: chỉ tắt <see cref="Movie.IsActive"/>. Xóa hẳn sẽ kéo theo suất chiếu và làm
    /// đơn đã đặt mất tham chiếu tới phim — khách không tra cứu lại được vé cũ.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var movie = await _db.Movies.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (movie is null) return NotFound();

        movie.IsActive = false;
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index));
    }
}
