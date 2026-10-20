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
    private readonly IBranchDirectory _branches;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;

    public MoviesController(
        CinemaDbContext db,
        IShowtimeQueryService showtimes,
        IBranchDirectory branches,
        IOptions<DisplayTimeZone> tz,
        TimeProvider clock)
    {
        _db = db;
        _showtimes = showtimes;
        _branches = branches;
        _tz = tz.Value;
        _clock = clock;
    }

    /// <param name="date">Ngày ở giờ rạp. Bỏ trống hoặc ngày không còn suất → ngày gần nhất còn suất.</param>
    /// <param name="city">Lọc theo thành phố của chi nhánh.</param>
    /// <param name="theaterId">Lọc theo một chi nhánh; thắng <paramref name="city"/> nếu cả hai cùng có.</param>
    public async Task<IActionResult> Details(
        int id, DateOnly? date, string? city, int? theaterId, CancellationToken ct)
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
        var today = _tz.ToLocalDate(nowUtc);
        var dates = await _showtimes.GetAvailableDatesAsync(id, today, ct: ct);

        // Chỉ nhận ngày có trong dải tab — ngày gõ tay trên URL mà không có suất thì về ngày đầu.
        DateOnly? selected = date is { } d && dates.Contains(d) ? d : dates.Count > 0 ? dates[0] : null;

        var branches = await _branches.GetActiveAsync(ct);
        var filter = BuildFilter(branches, city, theaterId);

        var groups = new List<BranchShowtimesViewModel>();
        if (selected is { } day)
        {
            var items = await _showtimes.GetForMovieOnDateAsync(id, day, ct: ct);

            // ShowtimeListItem chưa có City/TheaterId (việc B2.11), nên tra theo tên chi nhánh —
            // tên là unique index nên tra không nhầm. Chi nhánh đã đóng không có trong danh bạ
            // ⇒ suất của nó rơi ra luôn. B2.11 merge xong thì đọc thẳng từ item và bỏ bước này.
            var byName = branches.ToDictionary(b => b.Name);

            groups = items
                // Suất hôm nay đã bắt đầu thì không bán nữa (trang chọn ghế cũng trả 404).
                .Where(s => s.StartTimeUtc > nowUtc)
                .Where(s => byName.ContainsKey(s.TheaterName))
                .Select(s => (Showtime: s, Branch: byName[s.TheaterName]))
                .Where(x => filter.TheaterId is null || x.Branch.Id == filter.TheaterId)
                .Where(x => filter.City is null || x.Branch.City == filter.City)
                .GroupBy(x => x.Branch)
                .OrderBy(g => g.Key.City).ThenBy(g => g.Key.Name)
                .Select(g => new BranchShowtimesViewModel(
                    g.Key.Id, g.Key.Name, g.Key.City,
                    g.Select(x => new ShowtimeSlotViewModel(
                            x.Showtime.Id, x.Showtime.RoomName,
                            _tz.ToLocalTime(x.Showtime.StartTimeUtc), _tz.ToLocalTime(x.Showtime.EndTimeUtc),
                            x.Showtime.BasePrice))
                        .ToList()))
                .ToList();
        }

        ViewData["Title"] = movie.Card.Title;
        return View(new MovieDetailsViewModel(
            movie.Card, movie.Description, today, dates, selected, filter, groups));
    }

    /// <summary>
    /// Giá trị lọc đến từ URL nên chỉ giữ cái có thật: thành phố lạ hoặc id chi nhánh đã đóng
    /// coi như không lọc. Chọn chi nhánh thì thành phố suy ra từ chi nhánh đó.
    /// </summary>
    public static ShowtimeFilter BuildFilter(IReadOnlyList<BranchListItem> branches, string? city, int? theaterId)
    {
        var cities = branches.Select(b => b.City).Distinct().ToList();

        var branch = theaterId is null ? null : branches.FirstOrDefault(b => b.Id == theaterId);
        var selectedCity = branch?.City
            ?? cities.FirstOrDefault(c => string.Equals(c, city?.Trim(), StringComparison.OrdinalIgnoreCase));

        var options = branches
            .Where(b => selectedCity is null || b.City == selectedCity)
            .Select(b => new BranchOption(b.Id, b.Name, b.City))
            .ToList();

        return new ShowtimeFilter(selectedCity, branch?.Id, cities, options);
    }
}
