using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Services;

public sealed class ShowtimeQueryService : IShowtimeQueryService
{
    private readonly CinemaDbContext _db;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;

    public ShowtimeQueryService(CinemaDbContext db, IOptions<DisplayTimeZone> tz, TimeProvider clock)
    {
        _db = db;
        _tz = tz.Value;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ShowtimeListItem>> GetForMovieOnDateAsync(
        int movieId, DateOnly localDate, CancellationToken ct = default)
    {
        var all = await _db.Showtimes
            .Include(s => s.Movie)
            .Include(s => s.Room)!.ThenInclude(r => r!.Theater)
            .Where(s => s.MovieId == movieId)
            .ToListAsync(ct);

        return all
            .Where(s => _tz.ToLocalDate(s.StartTimeUtc) == localDate)
            .OrderBy(s => s.StartTimeUtc)
            .Select(s => new ShowtimeListItem(
                s.Id,
                s.MovieId,
                s.Movie!.Title,
                s.RoomId,
                s.Room!.RoomName,
                s.Room!.Theater!.Name,
                s.StartTimeUtc,
                s.EndTimeUtc,
                s.BasePrice))
            .ToList();
    }

    public async Task<IReadOnlyList<DateOnly>> GetAvailableDatesAsync(
        int movieId, DateOnly fromLocalDate, int dayCount = 7, CancellationToken ct = default)
    {
        var all = await _db.Showtimes
            .Where(s => s.MovieId == movieId)
            .ToListAsync(ct);

        // Suất đã bắt đầu thì không còn bán được nữa.
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var lastDate = fromLocalDate.AddDays(dayCount - 1);

        return all
            .Where(s => s.StartTimeUtc > nowUtc)
            .Select(s => _tz.ToLocalDate(s.StartTimeUtc))
            .Where(d => d >= fromLocalDate && d <= lastDate)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
    }
}
