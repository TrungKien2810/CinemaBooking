namespace CinemaBooking.Web.Services;

/// <summary>
/// Đọc lịch chiếu cho phía khách. Chỉ đọc — mọi thay đổi lịch đi qua Admin của Dev A.
/// Chi nhánh đã đóng (<c>Theater.IsActive = false</c>) không bao giờ hiện ra.
/// </summary>
public interface IShowtimeQueryService
{
    /// <summary>Suất chiếu của một phim trong đúng một ngày ở giờ rạp, sắp theo giờ bắt đầu.</summary>
    Task<IReadOnlyList<ShowtimeListItem>> GetForMovieOnDateAsync(
        int movieId, DateOnly localDate, ShowtimeQueryFilter? filter = null, CancellationToken ct = default);

    /// <summary>Các ngày còn suất chiếu (từ <paramref name="fromLocalDate"/> trở đi) để dựng dải tab ngày.</summary>
    Task<IReadOnlyList<DateOnly>> GetAvailableDatesAsync(
        int movieId, DateOnly fromLocalDate, int dayCount = 7, ShowtimeQueryFilter? filter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Các chi nhánh còn suất của phim trong cửa sổ ngày, sắp theo thành phố rồi tên — không phụ thuộc
    /// bộ lọc đang chọn, để dropdown không tự co lại sau khi khách lọc.
    /// </summary>
    Task<IReadOnlyList<ShowtimeBranchOption>> GetBranchesForMovieAsync(
        int movieId, DateOnly fromLocalDate, int dayCount = 7, CancellationToken ct = default);
}
