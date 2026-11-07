using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

public sealed class BranchDirectory : IBranchDirectory
{
    private readonly CinemaDbContext _db;

    public BranchDirectory(CinemaDbContext db) => _db = db;

    public async Task<IReadOnlyList<BranchListItem>> GetActiveAsync(CancellationToken ct = default) =>
        await _db.Theaters.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.City)
            .ThenBy(t => t.Name)
            .Select(t => new BranchListItem(t.Id, t.Name, t.City, t.Address, t.Rooms.Count))
            .ToListAsync(ct);

    public async Task<BranchDetails?> GetDetailsAsync(int id, DateTime nowUtc, DateOnly today, CancellationToken ct = default)
    {
        var theater = await _db.Theaters.AsNoTracking()
            .Where(t => t.Id == id && t.IsActive)
            .Select(t => new { t.Id, t.Name, t.City, t.Address, t.Phone, t.Email, t.OpeningHours, t.Description })
            .FirstOrDefaultAsync(ct);
        if (theater is null) return null;

        var rooms = await _db.Rooms.AsNoTracking()
            .Where(r => r.TheaterId == id)
            .OrderBy(r => r.RoomName)
            .Select(r => new BranchRoomInfo(
                r.RoomName, r.TotalRows, r.TotalColumns, r.Seats.Count,
                r.Seats.Count(s => s.SeatType == SeatType.Vip),
                r.Seats.Count(s => s.SeatType == SeatType.Couple)))
            .ToListAsync(ct);

        // Chỉ tính suất khách còn đặt được: chưa chiếu, chưa bị ẩn, phim còn hoạt động.
        var showtimes = await _db.Showtimes.AsNoTracking()
            .Where(s => s.Room!.TheaterId == id && s.IsActive && s.StartTimeUtc > nowUtc && s.Movie!.IsActive)
            .Select(s => new
            {
                s.MovieId, s.Movie!.Title, s.Movie.Genre, s.Movie.DurationMinutes, s.Movie.PosterUrl,
                s.StartTimeUtc, s.BasePrice,
            })
            .ToListAsync(ct);

        var movies = showtimes
            .GroupBy(s => new { s.MovieId, s.Title, s.Genre, s.DurationMinutes, s.PosterUrl })
            .Select(g => new BranchMovieInfo(
                g.Key.MovieId, g.Key.Title, g.Key.Genre, g.Key.DurationMinutes, g.Key.PosterUrl,
                g.Count(), g.Min(s => s.StartTimeUtc), g.Min(s => s.BasePrice)))
            .OrderBy(m => m.NextShowtimeUtc)
            .ToList();

        var promotionRows = await _db.Promotions.AsNoTracking()
            .Where(p => p.TheaterId == id && p.IsActive && p.StartDate <= today && p.EndDate >= today)
            .OrderBy(p => p.EndDate)
            .ToListAsync(ct);
        var promotions = promotionRows
            .Select(p => new BranchPromotionInfo(
                p.Id, p.Title, p.Description, p.Badge, p.StartDate, p.EndDate,
                p.AppliesAutomatically ? PromotionRules.Describe(p) : null))
            .ToList();

        return new BranchDetails(
            theater.Id, theater.Name, theater.City, theater.Address, rooms, movies, promotions,
            theater.Phone, theater.Email, theater.OpeningHours, theater.Description);
    }
}
