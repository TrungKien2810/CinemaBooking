using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Controllers;

/// <summary>Trang phim phía khách. Khác <c>Areas/Admin/.../MoviesController</c> (CRUD của admin).</summary>
public class MoviesController : Controller
{
    private readonly CinemaDbContext _db;
    private readonly IShowtimeQueryService _showtimes;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;

    public MoviesController(
        CinemaDbContext db,
        IShowtimeQueryService showtimes,
        IOptions<DisplayTimeZone> tz,
        TimeProvider clock)
    {
        _db = db;
        _showtimes = showtimes;
        _tz = tz.Value;
        _clock = clock;
    }

    /// <param name="date">Ngày ở giờ rạp. Bỏ trống hoặc ngày không còn suất → ngày gần nhất còn suất.</param>
    public async Task<IActionResult> Details(int id, DateOnly? date, CancellationToken ct)
    {
        var movie = await _db.Movies.AsNoTracking()
            .Where(m => m.Id == id && m.IsActive)
            .Select(m => new
            {
                Card = new MovieCardViewModel(m.Id, m.Title, m.Genre, m.DurationMinutes, m.ReleaseDate, m.PosterUrl),
                m.Description,
            })
            .FirstOrDefaultAsync(ct);

        // Phim đã ẩn cũng 404: khách không được mở trang đặt vé cho phim admin đã gỡ.
        if (movie is null) return NotFound();

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var dates = await _showtimes.GetAvailableDatesAsync(id, _tz.ToLocalDate(nowUtc), ct: ct);

        // Chỉ nhận ngày có trong dải tab — ngày gõ tay trên URL mà không có suất thì về ngày đầu.
        DateOnly? selected = date is { } d && dates.Contains(d) ? d : dates.Count > 0 ? dates[0] : null;

        var branches = new List<BranchShowtimesViewModel>();
        if (selected is { } day)
        {
            var items = await _showtimes.GetForMovieOnDateAsync(id, day, ct);

            branches = items
                // Suất hôm nay đã bắt đầu thì không bán nữa (trang chọn ghế cũng trả 404).
                .Where(s => s.StartTimeUtc > nowUtc)
                .GroupBy(s => s.TheaterName)
                .OrderBy(g => g.Key)
                .Select(g => new BranchShowtimesViewModel(
                    g.Key,
                    g.Select(s => new ShowtimeSlotViewModel(
                            s.Id, s.RoomName, _tz.ToLocalTime(s.StartTimeUtc), _tz.ToLocalTime(s.EndTimeUtc), s.BasePrice))
                        .ToList()))
                .ToList();
        }

        ViewData["Title"] = movie.Card.Title;
        return View(new MovieDetailsViewModel(movie.Card, movie.Description, dates, selected, branches));
    }
}
