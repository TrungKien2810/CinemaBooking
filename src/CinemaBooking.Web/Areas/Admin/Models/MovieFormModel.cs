using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Web.Areas.Admin.Models;

/// <summary>
/// Dữ liệu form thêm/sửa phim. Không bind thẳng vào entity để form không ghi đè được Id
/// hay các navigation của <see cref="Movie"/>.
/// </summary>
public sealed class MovieFormModel
{
    public int? Id { get; set; }

    [Display(Name = "Tên phim")]
    [Required(ErrorMessage = "Nhập tên phim.")]
    [StringLength(200, ErrorMessage = "Tên phim tối đa 200 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Mô tả")]
    [StringLength(2000, ErrorMessage = "Mô tả tối đa 2000 ký tự.")]
    public string? Description { get; set; }

    [Display(Name = "Thể loại")]
    [StringLength(100, ErrorMessage = "Thể loại tối đa 100 ký tự.")]
    public string? Genre { get; set; }

    [Display(Name = "Thời lượng (phút)")]
    [Range(1, 600, ErrorMessage = "Thời lượng từ 1 đến 600 phút.")]
    public int DurationMinutes { get; set; } = 120;

    [Display(Name = "Ngày khởi chiếu")]
    [Required(ErrorMessage = "Chọn ngày khởi chiếu.")]
    public DateOnly? ReleaseDate { get; set; }

    [Display(Name = "Ảnh poster (URL)")]
    [StringLength(500, ErrorMessage = "URL poster tối đa 500 ký tự.")]
    [RegularExpression(@"^(https?://|/)\S+$", ErrorMessage = "Poster phải là URL http(s) hoặc đường dẫn bắt đầu bằng /.")]
    public string? PosterUrl { get; set; }

    /// <summary>Ảnh tải lên từ máy; có file thì ghi đè <see cref="PosterUrl"/>.</summary>
    [Display(Name = "Hoặc tải ảnh lên")]
    public IFormFile? PosterFile { get; set; }

    [Display(Name = "Đang chiếu")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Phân loại độ tuổi")]
    public AgeRating AgeRating { get; set; } = AgeRating.P;

    [Display(Name = "Trailer (URL)")]
    [StringLength(500, ErrorMessage = "URL trailer tối đa 500 ký tự.")]
    [Url(ErrorMessage = "URL trailer không hợp lệ.")]
    public string? TrailerUrl { get; set; }

    [Display(Name = "Đạo diễn")]
    [StringLength(200, ErrorMessage = "Tên đạo diễn tối đa 200 ký tự.")]
    public string? Director { get; set; }

    [Display(Name = "Diễn viên (cách nhau bằng dấu phẩy)")]
    [StringLength(500, ErrorMessage = "Danh sách diễn viên tối đa 500 ký tự.")]
    public string? Cast { get; set; }

    public static MovieFormModel From(Movie m) => new()
    {
        Id = m.Id,
        Title = m.Title,
        Description = m.Description,
        Genre = m.Genre,
        DurationMinutes = m.DurationMinutes,
        ReleaseDate = m.ReleaseDate,
        PosterUrl = m.PosterUrl,
        IsActive = m.IsActive,
        AgeRating = m.AgeRating,
        TrailerUrl = m.TrailerUrl,
        Director = m.Director,
        Cast = m.Cast,
    };

    public void ApplyTo(Movie m)
    {
        m.Title = Title.Trim();
        m.Description = Description;
        m.Genre = string.IsNullOrWhiteSpace(Genre) ? null : Genre.Trim();
        m.DurationMinutes = DurationMinutes;
        m.ReleaseDate = ReleaseDate!.Value;
        m.PosterUrl = string.IsNullOrWhiteSpace(PosterUrl) ? null : PosterUrl.Trim();
        m.IsActive = IsActive;
        m.AgeRating = AgeRating;
        m.TrailerUrl = string.IsNullOrWhiteSpace(TrailerUrl) ? null : TrailerUrl.Trim();
        m.Director = string.IsNullOrWhiteSpace(Director) ? null : Director.Trim();
        m.Cast = string.IsNullOrWhiteSpace(Cast) ? null : Cast.Trim();
    }
}

/// <summary>Danh sách phim đã lọc + bộ lọc đang chọn.</summary>
public sealed record MoviesIndexViewModel(
    List<Movie> Items, string? Query, string? Status, string? Genre, IReadOnlyList<string> Genres, int Total, int ActiveTotal)
{
    /// <summary>Ngày hiện tại theo giờ rạp, để phân biệt phim đang chiếu với phim sắp chiếu.</summary>
    public DateOnly Today { get; init; }

    public bool IsFiltered => Query is not null || Status is not null || Genre is not null;
}
