using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Entities;

namespace CinemaBooking.Web.Areas.Admin.Models;

public sealed class PromotionFormModel : IValidatableObject
{
    public int? Id { get; set; }
    public int TheaterId { get; set; }
    public string TheaterName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập tên chương trình.")]
    [MaxLength(150, ErrorMessage = "Tối đa 150 ký tự.")]
    [Display(Name = "Tên chương trình")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000, ErrorMessage = "Tối đa 1000 ký tự.")]
    [Display(Name = "Mô tả")]
    public string? Description { get; set; }

    [MaxLength(40, ErrorMessage = "Tối đa 40 ký tự.")]
    [Display(Name = "Nhãn nổi bật (vd: -20%, Đồng giá 60k)")]
    public string? Badge { get; set; }

    [Required(ErrorMessage = "Chọn ngày bắt đầu.")]
    [Display(Name = "Từ ngày")]
    public DateOnly? StartDate { get; set; }

    [Required(ErrorMessage = "Chọn ngày kết thúc.")]
    [Display(Name = "Đến ngày")]
    public DateOnly? EndDate { get; set; }

    [Display(Name = "Đang áp dụng")]
    public bool IsActive { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (StartDate is not null && EndDate is not null && EndDate < StartDate)
            yield return new ValidationResult("Ngày kết thúc phải sau ngày bắt đầu.", [nameof(EndDate)]);
    }

    public static PromotionFormModel From(Promotion p, string theaterName) => new()
    {
        Id = p.Id, TheaterId = p.TheaterId, TheaterName = theaterName, Title = p.Title,
        Description = p.Description, Badge = p.Badge, StartDate = p.StartDate, EndDate = p.EndDate, IsActive = p.IsActive,
    };
}

public sealed record PromotionListItem(
    int Id, string Title, string? Badge, DateOnly StartDate, DateOnly EndDate, bool IsActive, bool IsRunning);

public sealed record PromotionsIndexViewModel(int TheaterId, string TheaterName, List<PromotionListItem> Items);
