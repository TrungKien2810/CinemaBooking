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
        // Đổi "một ngày ở giờ rạp" thành nửa khoảng UTC trước khi truy vấn: so sánh trở thành
        // một range scan chạy được trên index (RoomId, StartTimeUtc), thay vì phải đọc cả bảng
        // rồi cộng offset từng dòng trong RAM.
        var (fromUtc, toUtc) = _tz.DayRangeUtc(localDate);

        return await _db.Showtimes.AsNoTracking()
            .Where(s => s.MovieId == movieId
                && s.StartTimeUtc >= fromUtc
                && s.StartTimeUtc < toUtc)
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
                s.BasePrice))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DateOnly>> GetAvailableDatesAsync(
        int movieId, DateOnly fromLocalDate, int dayCount = 7, CancellationToken ct = default)
    {
        var (windowFromUtc, _) = _tz.DayRangeUtc(fromLocalDate);
        var (_, windowToUtc) = _tz.DayRangeUtc(fromLocalDate.AddDays(dayCount - 1));

        // Suất đã bắt đầu thì không bán được nữa — cắt luôn ở DB.
        var lowerBoundUtc = _clock.GetUtcNow().UtcDateTime;
        if (windowFromUtc > lowerBoundUtc) lowerBoundUtc = windowFromUtc;

        var startTimes = await _db.Showtimes.AsNoTracking()
            .Where(s => s.MovieId == movieId
                && s.StartTimeUtc > lowerBoundUtc
                && s.StartTimeUtc < windowToUtc)
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
}
