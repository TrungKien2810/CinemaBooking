namespace CinemaBooking.Web.Services;

/// <summary>Đọc danh mục phim cho phía khách. Chỉ trả về phim đang chiếu (IsActive).</summary>
public interface IMovieCatalogService
{
    Task<IReadOnlyList<MovieListItem>> SearchAsync(
        MovieSearchQuery query, CancellationToken ct = default);

    /// <summary>Danh sách thể loại có thật trong dữ liệu, để đổ vào dropdown lọc.</summary>
    Task<IReadOnlyList<string>> GetGenresAsync(CancellationToken ct = default);
}
