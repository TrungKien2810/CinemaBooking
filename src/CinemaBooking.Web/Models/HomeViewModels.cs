namespace CinemaBooking.Web.Models;

public sealed record HomeIndexViewModel(IReadOnlyList<MovieCardViewModel> Movies)
{
    public bool HasMovies => Movies.Count > 0;
}

/// <summary>
/// Chỉ những trường trang chủ thật sự vẽ ra. Không trả thẳng entity Movie về view:
/// Description dài 2000 ký tự và collection Showtimes đều không dùng tới ở đây.
///
/// Cùng bộ trường với MovieListItem của Dev B (IMovieCatalogService) nhưng khác vai trò:
/// MovieListItem là DTO của tầng service, còn bản này là view model — nó giữ phần hiển thị
/// (ảnh mặc định, nhãn thể loại) để DTO của service không phải biết gì về đường dẫn ảnh.
/// Sprint 2 khi trang chủ chuyển sang gọi service, controller chỉ cần map DTO sang đây.
/// </summary>
public sealed record MovieCardViewModel(
    int Id,
    string Title,
    string? Genre,
    int DurationMinutes,
    DateOnly ReleaseDate,
    string? PosterUrl)
{
    /// <summary>Ảnh thay thế khi phim chưa có poster hoặc file poster không tải được.</summary>
    public const string PlaceholderPoster = "/img/poster-placeholder.svg";

    /// <summary>Không có poster thì trả ảnh mặc định — view không phải tự kiểm tra null.</summary>
    public string PosterOrPlaceholder =>
        string.IsNullOrWhiteSpace(PosterUrl) ? PlaceholderPoster : PosterUrl;

    public string GenreOrDefault => string.IsNullOrWhiteSpace(Genre) ? "Chưa phân loại" : Genre;
}
