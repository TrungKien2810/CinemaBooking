using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

public sealed class ShowtimeScheduler : IShowtimeScheduler
{
    private readonly CinemaDbContext _db;

    public ShowtimeScheduler(CinemaDbContext db) => _db = db;

    public async Task PrepareAsync(Showtime showtime, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(showtime);

        var duration = await _db.Movies.AsNoTracking()
            .Where(m => m.Id == showtime.MovieId)
            .Select(m => (int?)m.DurationMinutes)
            .FirstOrDefaultAsync(ct);

        if (duration is null)
            throw new ShowtimeSchedulingException($"Không tìm thấy phim #{showtime.MovieId}.");

        showtime.StartTimeUtc = AsUtc(showtime.StartTimeUtc);
        showtime.EndTimeUtc = ComputeEndTimeUtc(showtime.StartTimeUtc, duration.Value);
    }

    /// <summary>
    /// Giờ kết thúc = giờ bắt đầu + thời lượng phim. Cố ý KHÔNG cộng thêm thời gian dọn phòng:
    /// khoảng đệm giữa hai suất là luật của kiểm tra trùng lịch (A2.8), không phải của phim.
    /// </summary>
    public static DateTime ComputeEndTimeUtc(DateTime startUtc, int durationMinutes)
    {
        if (durationMinutes <= 0)
            throw new ShowtimeSchedulingException("Thời lượng phim phải lớn hơn 0 phút.");

        return AsUtc(startUtc).AddMinutes(durationMinutes);
    }

    /// <summary>
    /// Giờ từ form model binding về mang Kind = Unspecified. Đóng dấu Utc để so sánh và lưu
    /// không bị lệch theo múi giờ của máy chủ.
    /// </summary>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
