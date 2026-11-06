using System.ComponentModel.DataAnnotations;
using CinemaBooking.Web.Services;

namespace CinemaBooking.Web.Areas.Admin.Models;

/// <summary>Dữ liệu form "Gợi ý lịch chiếu cả ngày".</summary>
public sealed class AutoPlanForm : IValidatableObject
{
    public int? TheaterId { get; set; }

    [Required(ErrorMessage = "Chọn ngày cần xếp lịch.")]
    [Display(Name = "Ngày chiếu")]
    public DateOnly? Date { get; set; }

    private decimal _basePrice = 80_000m;
    private decimal _primeSurcharge = 20_000m;

    [Range(10_000, 1_000_000, ErrorMessage = "Giá vé cơ bản từ 10.000đ đến 1.000.000đ (gõ 90 = 90.000đ).")]
    [Display(Name = "Giá vé cơ bản")]
    public decimal BasePrice { get => _basePrice; set => _basePrice = MoneyFormat.ExpandThousands(value); }

    [Range(0, 500_000, ErrorMessage = "Phụ thu giờ vàng từ 0 đến 500.000đ (gõ 20 = 20.000đ).")]
    [Display(Name = "Phụ thu giờ vàng")]
    public decimal PrimeSurcharge { get => _primeSurcharge; set => _primeSurcharge = MoneyFormat.ExpandThousands(value); }

    [Display(Name = "Ngôn ngữ các suất")]
    public CinemaBooking.Domain.Enums.ShowLanguage Language { get; set; } = CinemaBooking.Domain.Enums.ShowLanguage.Subtitled;

    /// <summary>Phim được đưa vào lịch.</summary>
    public List<int> MovieIds { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (MovieIds.Count == 0)
            yield return new ValidationResult("Chọn ít nhất một phim.", [nameof(MovieIds)]);
    }
}

public sealed record AutoPlanMovieOption(int Id, string Title, int DurationMinutes, DateOnly ReleaseDate, bool Selected);

public sealed record AutoPlanTheaterOption(int Id, string Name);

public sealed record AutoPlanRoomSummary(string Name, int Capacity, int Shows);

public sealed record AutoPlanViewModel(
    AutoPlanForm Form,
    string TheaterName,
    IReadOnlyList<AutoPlanTheaterOption> Theaters,
    IReadOnlyList<AutoPlanMovieOption> Movies,
    IReadOnlyList<PlannedShowtime>? Proposed,
    IReadOnlyList<AutoPlanRoomSummary> RoomSummaries,
    decimal PrimeSurcharge);
