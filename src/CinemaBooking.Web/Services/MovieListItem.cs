namespace CinemaBooking.Web.Services;

/// <summary>Một phim ở dạng đủ để vẽ thẻ phim ngoài trang chủ.</summary>
public sealed record MovieListItem(
    int Id,
    string Title,
    string? Genre,
    int DurationMinutes,
    DateOnly ReleaseDate,
    string? PosterUrl);

/// <summary>Tiêu chí lọc phim đến từ query string của trang chủ.</summary>
public sealed record MovieSearchQuery(string? Keyword = null, string? Genre = null)
{
    public static readonly MovieSearchQuery All = new();

    /// <summary>Chuẩn hoá đầu vào từ URL: chuỗi rỗng và khoảng trắng thừa coi như không lọc.</summary>
    public MovieSearchQuery Normalized() => new(
        string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
        string.IsNullOrWhiteSpace(Genre) ? null : Genre.Trim());
}
