using CinemaBooking.Domain.Entities;
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
        int movieId, DateOnly localDate, ShowtimeQueryFilter? filter = null, CancellationToken ct = default)
    {
        // Đổi "một ngày ở giờ rạp" thành nửa khoảng UTC trước khi truy vấn: so sánh trở thành
        // một range scan chạy được trên index (RoomId, StartTimeUtc), thay vì phải đọc cả bảng
        // rồi cộng offset từng dòng trong RAM.
        var (fromUtc, toUtc) = _tz.DayRangeUtc(localDate);

        return await ForMovie(movieId, filter)
            .Where(s => s.StartTimeUtc >= fromUtc && s.StartTimeUtc < toUtc)
            .OrderBy(s => s.StartTimeUtc)
            .Select(s => new ShowtimeListItem(
                s.Id,
                s.MovieId,
                s.Movie!.Title,
                s.RoomId,
                s.Room!.RoomName,
                s.Room!.TheaterId,
                s.Room!.Theater!.Name,
                s.Room!.Theater!.City,
                s.StartTimeUtc,
                s.EndTimeUtc,
                s.BasePrice,
                s.Room!.RoomType,
                s.Language))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DateOnly>> GetAvailableDatesAsync(
        int movieId, DateOnly fromLocalDate, int dayCount = 7, ShowtimeQueryFilter? filter = null,
        CancellationToken ct = default)
    {
        var startTimes = await UpcomingInWindow(ForMovie(movieId, filter), fromLocalDate, dayCount)
            .Select(s => s.StartTimeUtc)
            .Distinct()
            .ToListAsync(ct);

        // Chỉ phần đổi UTC → ngày địa phương ở lại RAM, và chỉ trên tập đã lọc sẵn.
        return startTimes
            .Select(_tz.ToLocalDate)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
    }

    public async Task<IReadOnlyList<ShowtimeBranchOption>> GetBranchesForMovieAsync(
        int movieId, DateOnly fromLocalDate, int dayCount = 7, CancellationToken ct = default)
    {
        // Anonymous type thay vì record: EF dịch được DISTINCT + ORDER BY trên nó, còn thuộc tính
        // của record tạo qua constructor thì không map ngược lại thành cột được.
        var rows = await UpcomingInWindow(ForMovie(movieId, ShowtimeQueryFilter.None), fromLocalDate, dayCount)
            .Select(s => new { s.Room!.TheaterId, s.Room!.Theater!.Name, s.Room!.Theater!.City })
            .Distinct()
            .OrderBy(b => b.City).ThenBy(b => b.Name)
            .ToListAsync(ct);

        return rows.Select(b => new ShowtimeBranchOption(b.TheaterId, b.Name, b.City)).ToList();
    }

    /// <summary>Suất của một phim ở chi nhánh còn hoạt động, đã áp bộ lọc — mọi điều kiện đều dịch ra SQL.</summary>
    private IQueryable<Showtime> ForMovie(int movieId, ShowtimeQueryFilter? filter)
    {
        var query = _db.Showtimes.AsNoTracking()
            .Where(s => s.MovieId == movieId && s.IsActive && s.Room!.Theater!.IsActive);

        if (!string.IsNullOrWhiteSpace(filter?.City))
        {
            var city = filter.City.Trim();
            query = query.Where(s => s.Room!.Theater!.City == city);
        }

        if (filter?.TheaterId is int theaterId)
            query = query.Where(s => s.Room!.TheaterId == theaterId);

        return query;
    }

    private IQueryable<Showtime> UpcomingInWindow(IQueryable<Showtime> query, DateOnly fromLocalDate, int dayCount)
    {
        var (windowFromUtc, _) = _tz.DayRangeUtc(fromLocalDate);
        var (_, windowToUtc) = _tz.DayRangeUtc(fromLocalDate.AddDays(dayCount - 1));

        // Suất đã bắt đầu thì không bán được nữa — cắt luôn ở DB.
        var lowerBoundUtc = _clock.GetUtcNow().UtcDateTime;
        if (windowFromUtc > lowerBoundUtc) lowerBoundUtc = windowFromUtc;

        return query.Where(s => s.StartTimeUtc > lowerBoundUtc && s.StartTimeUtc < windowToUtc);
    }
}
