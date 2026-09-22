using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

public class MoviesController : AdminControllerBase
{
    private readonly CinemaDbContext _db;

    public MoviesController(CinemaDbContext db) => _db = db;

    /// <summary>Liệt kê cả phim đã ẩn — admin cần thấy để bật lại.</summary>
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var movies = await _db.Movies.AsNoTracking()
            .OrderByDescending(m => m.IsActive)
            .ThenByDescending(m => m.ReleaseDate)
            .ThenBy(m => m.Title)
            .ToListAsync(ct);

        return View(movies);
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
