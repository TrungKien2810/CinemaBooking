using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Web.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CinemaBooking.Web.Areas.Admin.Models;

public sealed class VoucherFormModel : IValidatableObject
{
    public int? Id { get; set; }

    [Display(Name = "Mã giảm giá")]
    [Required(ErrorMessage = "Nhập mã giảm giá.")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "Mã dài 3–30 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Tên chương trình")]
    [Required(ErrorMessage = "Nhập tên chương trình.")]
    [StringLength(150, ErrorMessage = "Tối đa 150 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Mô tả")]
    [StringLength(500, ErrorMessage = "Tối đa 500 ký tự.")]
    public string? Description { get; set; }

    [Display(Name = "Áp dụng cho chi nhánh")]
    public int? TheaterId { get; set; }

    /// <summary>"percent" = giảm theo %, "amount" = giảm số tiền cố định.</summary>
    [Display(Name = "Cách giảm")]
    public string DiscountKind { get; set; } = "percent";

    [Display(Name = "Giảm (%)")]
    [Range(1, 100, ErrorMessage = "Phần trăm giảm từ 1 đến 100.")]
    public int? DiscountPercent { get; set; }

    private decimal? _discountAmount;
    private decimal? _maxDiscount;
    private decimal? _minOrder;

    [Display(Name = "Số tiền giảm")]
    [Range(1000, 10_000_000, ErrorMessage = "Số tiền giảm từ 1.000đ đến 10.000.000đ (gõ 30 = 30.000đ).")]
    public decimal? DiscountAmount { get => _discountAmount; set => _discountAmount = Expand(value); }

    [Display(Name = "Giảm tối đa")]
    [Range(1000, 10_000_000, ErrorMessage = "Mức giảm tối đa từ 1.000đ đến 10.000.000đ.")]
    public decimal? MaxDiscountAmount { get => _maxDiscount; set => _maxDiscount = Expand(value); }

    [Display(Name = "Đơn tối thiểu")]
    [Range(0, 100_000_000, ErrorMessage = "Đơn tối thiểu không hợp lệ.")]
    public decimal? MinOrderAmount { get => _minOrder; set => _minOrder = Expand(value); }

    [Display(Name = "Từ ngày")]
    [Required(ErrorMessage = "Chọn ngày bắt đầu.")]
    public DateOnly? StartDate { get; set; }

    [Display(Name = "Đến ngày")]
    [Required(ErrorMessage = "Chọn ngày kết thúc.")]
    public DateOnly? EndDate { get; set; }

    [Display(Name = "Tổng số lượt dùng (để trống = không giới hạn)")]
    [Range(1, 1_000_000, ErrorMessage = "Số lượt từ 1 đến 1.000.000.")]
    public int? UsageLimit { get; set; }

    [Display(Name = "Số lần mỗi khách được dùng (0 = không giới hạn)")]
    [Range(0, 1000, ErrorMessage = "Từ 0 đến 1000.")]
    public int PerCustomerLimit { get; set; } = 1;

    [Display(Name = "Đang áp dụng")]
    public bool IsActive { get; set; } = true;

    [ValidateNever] public List<SelectListItem> Theaters { get; set; } = [];

    /// <summary>true khi người dùng là BranchAdmin: chi nhánh bị khóa vào chi nhánh của họ.</summary>
    [ValidateNever] public bool TheaterLocked { get; set; }

    private static decimal? Expand(decimal? v) => v is { } x ? MoneyFormat.ExpandThousands(x) : null;

    public static readonly Regex CodePattern = new("^[A-Z0-9][A-Z0-9_-]{2,29}$", RegexOptions.Compiled);

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        var code = Code.Trim().ToUpperInvariant();
        if (!CodePattern.IsMatch(code))
            yield return new ValidationResult("Mã chỉ gồm chữ không dấu, số, gạch ngang/gạch dưới (3–30 ký tự).", [nameof(Code)]);

        if (DiscountKind == "percent" && DiscountPercent is null)
            yield return new ValidationResult("Nhập phần trăm giảm.", [nameof(DiscountPercent)]);
        if (DiscountKind == "amount" && DiscountAmount is null)
            yield return new ValidationResult("Nhập số tiền giảm.", [nameof(DiscountAmount)]);

        if (StartDate is not null && EndDate is not null && EndDate < StartDate)
            yield return new ValidationResult("Ngày kết thúc phải sau ngày bắt đầu.", [nameof(EndDate)]);
    }

    public static VoucherFormModel From(Voucher v) => new()
    {
        Id = v.Id, Code = v.Code, Title = v.Title, Description = v.Description, TheaterId = v.TheaterId,
        DiscountKind = v.DiscountPercent is > 0 ? "percent" : "amount",
        DiscountPercent = v.DiscountPercent, DiscountAmount = v.DiscountAmount,
        MaxDiscountAmount = v.MaxDiscountAmount, MinOrderAmount = v.MinOrderAmount,
        StartDate = v.StartDate, EndDate = v.EndDate, UsageLimit = v.UsageLimit,
        PerCustomerLimit = v.PerCustomerLimit, IsActive = v.IsActive,
    };

    public void ApplyTo(Voucher v)
    {
        v.Code = Code.Trim().ToUpperInvariant();
        v.Title = Title.Trim();
        v.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        v.TheaterId = TheaterId;

        var percent = DiscountKind == "percent";
        v.DiscountPercent = percent ? DiscountPercent : null;
        v.DiscountAmount = percent ? null : DiscountAmount;
        v.MaxDiscountAmount = percent ? MaxDiscountAmount : null;   // trần chỉ có nghĩa khi giảm theo %
        v.MinOrderAmount = MinOrderAmount is > 0 ? MinOrderAmount : null;

        v.StartDate = StartDate!.Value;
        v.EndDate = EndDate!.Value;
        v.UsageLimit = UsageLimit;
        v.PerCustomerLimit = PerCustomerLimit;
        v.IsActive = IsActive;
    }
}

public sealed record VoucherListItem(
    int Id, string Code, string Title, string Scope, string Rule, DateOnly StartDate, DateOnly EndDate,
    int Used, int? UsageLimit, bool IsActive, bool IsRunning, bool CanEdit);

public sealed record VouchersIndexViewModel(
    List<VoucherListItem> Items, string? Query, string? Status, PageInfo Paging, int Total);
