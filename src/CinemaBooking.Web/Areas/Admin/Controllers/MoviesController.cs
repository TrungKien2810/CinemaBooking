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
    private const long MaxPosterBytes = 5 * 1024 * 1024;
    private const string PosterFolder = "img/posters/uploads";

    private static readonly Dictionary<string, byte[]> PosterSignatures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = [0xFF, 0xD8, 0xFF],
        [".jpeg"] = [0xFF, 0xD8, 0xFF],
        [".png"] = [0x89, 0x50, 0x4E, 0x47],
        [".webp"] = [0x52, 0x49, 0x46, 0x46],
    };

    private readonly CinemaDbContext _db;
    private readonly TimeProvider _clock;
    private readonly IWebHostEnvironment? _env;

    public MoviesController(CinemaDbContext db, TimeProvider? clock = null, IWebHostEnvironment? env = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _env = env;
    }

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
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime.AddHours(7));

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

        return View(new MoviesIndexViewModel(items, keyword, statusFilter, selectedGenre, genres, all.Count, all.Count(m => m.IsActive))
        {
            Today = today,
        });
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new MovieFormModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MovieFormModel form, CancellationToken ct)
    {
        await ValidateNotDuplicateAsync(form, ct);
        await SavePosterAsync(form, ct);
        if (!ModelState.IsValid) return View("Form", form);

        var movie = new Movie();
        form.ApplyTo(movie);
        _db.Movies.Add(movie);
        await _db.SaveChangesAsync(ct);

        Notify($"Đã thêm phim “{movie.Title}”.");
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
        await ValidateNotDuplicateAsync(form, ct);
        await SavePosterAsync(form, ct);

        // Bỏ tick "Đang chiếu" trong form cũng là ẩn phim: phải qua đúng quy tắc như nút Ẩn.
        if (movie.IsActive && !form.IsActive && await UpcomingTicketsAsync(id, ct) is > 0 and var sold)
            ModelState.AddModelError(nameof(form.IsActive), HideBlockedMessage(sold));

        if (!ModelState.IsValid) return View("Form", form);

        form.ApplyTo(movie);
        await _db.SaveChangesAsync(ct);

        Notify($"Đã lưu phim “{movie.Title}”.");
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

        if (await UpcomingTicketsAsync(id, ct) is > 0 and var sold)
        {
            NotifyError(HideBlockedMessage(sold));
            return RedirectToAction(nameof(Index));
        }

        movie.IsActive = false;
        await _db.SaveChangesAsync(ct);

        Notify($"Đã ẩn phim “{movie.Title}” khỏi trang khách.");
        return RedirectToAction(nameof(Index));
    }

    private Task<int> UpcomingTicketsAsync(int movieId, CancellationToken ct) =>
        SoldTickets.UpcomingForMovieAsync(_db, movieId, _clock.GetUtcNow().UtcDateTime, ct);

    private static string HideBlockedMessage(int sold) =>
        $"Phim còn {sold} đơn của các suất chưa chiếu nên chưa ẩn được. Đợi chiếu xong, hoặc hủy các suất đó ở mục Suất chiếu.";

    /// <summary>Cùng tên và cùng ngày khởi chiếu mới coi là trùng: phim làm lại có thể trùng tên nhưng khác ngày.</summary>
    private async Task ValidateNotDuplicateAsync(MovieFormModel form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(form.Title) || form.ReleaseDate is null) return;

        var title = TextSearch.Normalize(form.Title);
        var sameDay = await _db.Movies.AsNoTracking()
            .Where(m => m.ReleaseDate == form.ReleaseDate && m.Id != form.Id)
            .Select(m => m.Title)
            .ToListAsync(ct);
        if (sameDay.Any(t => TextSearch.Normalize(t) == title))
            ModelState.AddModelError(nameof(form.Title), "Đã có phim cùng tên và cùng ngày khởi chiếu.");
    }

    /// <summary>Lưu ảnh tải lên vào wwwroot và trỏ PosterUrl vào đó. Kiểm tra cả đuôi file lẫn chữ ký nhị phân.</summary>
    private async Task SavePosterAsync(MovieFormModel form, CancellationToken ct)
    {
        if (form.PosterFile is not { Length: > 0 } file || _env is null) return;

        var extension = Path.GetExtension(file.FileName);
        if (!PosterSignatures.TryGetValue(extension, out var signature))
        {
            ModelState.AddModelError(nameof(form.PosterFile), "Chỉ nhận ảnh JPG, PNG hoặc WEBP.");
            return;
        }
        if (file.Length > MaxPosterBytes)
        {
            ModelState.AddModelError(nameof(form.PosterFile), "Ảnh tối đa 5 MB.");
            return;
        }

        await using var input = file.OpenReadStream();
        var head = new byte[signature.Length];
        await input.ReadExactlyAsync(head, ct);
        if (!head.SequenceEqual(signature))
        {
            ModelState.AddModelError(nameof(form.PosterFile), "File không phải ảnh hợp lệ.");
            return;
        }
        input.Position = 0;

        var folder = Path.Combine(_env.WebRootPath, PosterFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folder);
        var name = Guid.NewGuid().ToString("N") + extension.ToLowerInvariant();
        await using (var output = System.IO.File.Create(Path.Combine(folder, name)))
            await input.CopyToAsync(output, ct);

        form.PosterUrl = $"/{PosterFolder}/{name}";
        ModelState.Remove(nameof(form.PosterUrl));
    }
}
