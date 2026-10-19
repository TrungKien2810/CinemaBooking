using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Entities;

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
    public string? PosterUrl { get; set; }

    [Display(Name = "Đang chiếu")]
    public bool IsActive { get; set; } = true;

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
    }
}
