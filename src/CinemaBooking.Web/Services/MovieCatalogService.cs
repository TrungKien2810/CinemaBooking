using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

public sealed class MovieCatalogService : IMovieCatalogService
{
    private readonly CinemaDbContext _db;

    public MovieCatalogService(CinemaDbContext db) => _db = db;

    public async Task<IReadOnlyList<MovieListItem>> SearchAsync(
        MovieSearchQuery query, CancellationToken ct = default)
    {
        var q = query.Normalized();

        var movies = await _db.Movies
            .Where(m => m.IsActive)
            .ToListAsync(ct);

        return movies
            .Where(m => q.Keyword is null
                || m.Title.Contains(q.Keyword, StringComparison.OrdinalIgnoreCase)
                || (m.Genre != null && m.Genre.Contains(q.Keyword, StringComparison.OrdinalIgnoreCase)))
            .Where(m => q.Genre is null
                || string.Equals(m.Genre, q.Genre, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.ReleaseDate)
            .ThenBy(m => m.Title)
            .Select(m => new MovieListItem(
                m.Id, m.Title, m.Genre, m.DurationMinutes, m.ReleaseDate, m.PosterUrl))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetGenresAsync(CancellationToken ct = default)
    {
        var movies = await _db.Movies.Where(m => m.IsActive).ToListAsync(ct);

        return movies
            .Select(m => m.Genre)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g)
            .ToList();
    }
}
