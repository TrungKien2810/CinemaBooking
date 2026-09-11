namespace CinemaBooking.Web.Models;

public class HomeIndexViewModel
{
    public IReadOnlyList<MovieCardViewModel> Movies { get; init; } = [];

    public bool HasMovies => Movies.Count > 0;
}

/// <summary>
/// Chỉ những trường trang chủ thật sự vẽ ra. Không trả thẳng entity Movie về view:
/// Description dài 2000 ký tự và collection Showtimes đều không dùng tới ở đây.
/// </summary>
public class MovieCardViewModel
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Genre { get; init; }
    public int DurationMinutes { get; init; }
    public string? PosterUrl { get; init; }
    public DateOnly ReleaseDate { get; init; }
}
