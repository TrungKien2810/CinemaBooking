using CinemaBooking.Domain.Entities;
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
        // Lọc, sắp xếp và chiếu cột đều nằm trong IQueryable ⇒ EF dịch trọn thành một câu SQL.
        // Đừng chèn ToList()/AsEnumerable()/AsParallel() vào giữa chuỗi này: xem ADR 0001.
        var normalized = query.Normalized();
        var source = _db.Movies.AsNoTracking();

        // Bỏ dấu tiếng Việt không dịch được sang SQL: danh mục phim nhỏ nên so khớp từ khóa trên RAM để lấy id,
        // phần lọc còn lại vẫn trong một câu SQL.
        if (normalized.Keyword is { } keyword)
        {
            var candidates = await source.Where(m => m.IsActive)
                .Select(m => new { m.Id, m.Title, m.Genre })
                .ToListAsync(ct);
            var ids = candidates.Where(m => TextSearch.Matches(keyword, m.Title, m.Genre)).Select(m => m.Id).ToList();
            source = source.Where(m => ids.Contains(m.Id));
            normalized = normalized with { Keyword = null };
        }

        return await Filter(source, normalized)
            .OrderByDescending(m => m.ReleaseDate)
            .ThenBy(m => m.Title)
            .Select(m => new MovieListItem(
                m.Id, m.Title, m.Genre, m.DurationMinutes, m.ReleaseDate, m.PosterUrl))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetGenresAsync(CancellationToken ct = default)
    {
        // DISTINCT chạy ở DB; chỉ vài chục thể loại đi qua dây.
        var genres = await _db.Movies.AsNoTracking()
            .Where(m => m.IsActive && m.Genre != null && m.Genre != string.Empty)
            .Select(m => m.Genre!)
            .Distinct()
            .ToListAsync(ct);

        return genres
            .Select(g => g.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.CurrentCulture)
            .ToList();
    }

    private static IQueryable<Movie> Filter(IQueryable<Movie> source, MovieSearchQuery q)
    {
        source = source.Where(m => m.IsActive);

        if (q.Genre is not null)
        {
            var genre = q.Genre.ToLower();
            source = source.Where(m => m.Genre != null && m.Genre.ToLower() == genre);
        }

        return source;
    }
}
