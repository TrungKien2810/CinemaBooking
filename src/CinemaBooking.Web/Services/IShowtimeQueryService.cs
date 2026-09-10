namespace CinemaBooking.Web.Services;

/// <summary>
/// Đọc lịch chiếu cho phía khách. Chỉ đọc — mọi thay đổi lịch đi qua Admin của Dev A.
/// </summary>
public interface IShowtimeQueryService
{
    /// <summary>Suất chiếu của một phim trong đúng một ngày ở giờ rạp, sắp theo giờ bắt đầu.</summary>
    Task<IReadOnlyList<ShowtimeListItem>> GetForMovieOnDateAsync(
        int movieId, DateOnly localDate, CancellationToken ct = default);

    /// <summary>Các ngày còn suất chiếu (từ <paramref name="fromLocalDate"/> trở đi) để dựng dải tab ngày.</summary>
    Task<IReadOnlyList<DateOnly>> GetAvailableDatesAsync(
        int movieId, DateOnly fromLocalDate, int dayCount = 7, CancellationToken ct = default);
}
