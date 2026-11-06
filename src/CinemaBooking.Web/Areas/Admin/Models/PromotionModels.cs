using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;

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

    // ── Giảm giá tự động ──

    /// <summary>Cách giảm: "none" (chỉ quảng bá), "percent" (giảm %), "fixed" (đồng giá).</summary>
    [Display(Name = "Cách giảm giá")]
    public string DiscountKind { get; set; } = "none";

    [Range(1, 99, ErrorMessage = "Phần trăm giảm từ 1 đến 99 (không có vé 0đ).")]
    [Display(Name = "Giảm (%)")]
    public int? DiscountPercent { get; set; }

    private decimal? _fixedPrice;

    [Range(1000, 10_000_000, ErrorMessage = "Giá đồng giá từ 1.000đ đến 10.000.000đ (gõ 60 = 60.000đ).")]
    [Display(Name = "Giá đồng giá (mỗi ghế)")]
    public decimal? FixedPrice { get => _fixedPrice; set => _fixedPrice = value is { } v ? MoneyFormat.ExpandThousands(v) : null; }

    [Display(Name = "Loại ghế áp dụng")]
    public SeatType? ApplicableSeatType { get; set; }

    /// <summary>Các thứ được áp dụng, theo <see cref="DayOfWeek"/> (0 = Chủ nhật … 6 = Thứ bảy); rỗng = mọi ngày.</summary>
    public List<int> Days { get; set; } = new();

    [Display(Name = "Suất bắt đầu từ giờ")]
    public TimeOnly? FromTime { get; set; }

    [Display(Name = "Suất bắt đầu trước giờ")]
    public TimeOnly? ToTime { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (StartDate is not null && EndDate is not null && EndDate < StartDate)
            yield return new ValidationResult("Ngày kết thúc phải sau ngày bắt đầu.", [nameof(EndDate)]);

        if (DiscountKind == "percent" && DiscountPercent is null)
            yield return new ValidationResult("Nhập phần trăm giảm.", [nameof(DiscountPercent)]);
        if (DiscountKind == "fixed" && FixedPrice is null)
            yield return new ValidationResult("Nhập giá đồng giá.", [nameof(FixedPrice)]);

        if (FromTime is not null && ToTime is not null && ToTime <= FromTime)
            yield return new ValidationResult("Giờ kết thúc phải sau giờ bắt đầu.", [nameof(ToTime)]);

        if (Days.Any(d => d is < 0 or > 6))
            yield return new ValidationResult("Thứ trong tuần không hợp lệ.", [nameof(Days)]);
    }

    public int DaysOfWeekMask => Days.Where(d => d is >= 0 and <= 6).Distinct().Aggregate(0, (mask, d) => mask | (1 << d));

    public static PromotionFormModel From(Promotion p, string theaterName) => new()
    {
        Id = p.Id, TheaterId = p.TheaterId, TheaterName = theaterName, Title = p.Title,
        Description = p.Description, Badge = p.Badge, StartDate = p.StartDate, EndDate = p.EndDate, IsActive = p.IsActive,
        DiscountKind = p.DiscountPercent is > 0 ? "percent" : p.FixedPrice is > 0 ? "fixed" : "none",
        DiscountPercent = p.DiscountPercent, FixedPrice = p.FixedPrice, ApplicableSeatType = p.ApplicableSeatType,
        Days = Enumerable.Range(0, 7).Where(d => (p.DaysOfWeekMask & (1 << d)) != 0).ToList(),
        FromTime = p.FromTime, ToTime = p.ToTime,
    };
}

public sealed record PromotionListItem(
    int Id, string Title, string? Badge, DateOnly StartDate, DateOnly EndDate, bool IsActive, bool IsRunning,
    string Rule = "");

public sealed record PromotionsIndexViewModel(int TheaterId, string TheaterName, List<PromotionListItem> Items);
