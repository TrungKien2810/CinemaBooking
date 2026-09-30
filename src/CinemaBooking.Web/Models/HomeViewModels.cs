namespace CinemaBooking.Web.Models;

/// <param name="Keyword">Từ khóa đang tìm (đã chuẩn hoá), để điền lại vào ô tìm kiếm.</param>
/// <param name="Genre">Thể loại đang lọc.</param>
/// <param name="Genres">Các thể loại có thật trong dữ liệu, để vẽ chip lọc.</param>
public sealed record HomeIndexViewModel(
    IReadOnlyList<MovieCardViewModel> Movies,
    string? Keyword = null,
    string? Genre = null,
    IReadOnlyList<string>? Genres = null)
{
    public bool HasMovies => Movies.Count > 0;

    public bool IsFiltered => Keyword is not null || Genre is not null;

    public IReadOnlyList<string> GenreOptions => Genres ?? [];
}

/// <summary>
/// Chỉ những trường trang chủ thật sự vẽ ra. Không trả thẳng entity Movie về view:
/// Description dài 2000 ký tự và collection Showtimes đều không dùng tới ở đây.
///
/// Cùng bộ trường với MovieListItem của Dev B (IMovieCatalogService) nhưng khác vai trò:
/// MovieListItem là DTO của tầng service, còn bản này là view model — nó giữ phần hiển thị
/// (ảnh mặc định, nhãn thể loại) để DTO của service không phải biết gì về đường dẫn ảnh.
/// Trang chủ gọi service rồi map DTO sang đây (<see cref="FromListItem"/>).
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

    public static MovieCardViewModel FromListItem(Services.MovieListItem m) =>
        new(m.Id, m.Title, m.Genre, m.DurationMinutes, m.ReleaseDate, m.PosterUrl);
}
