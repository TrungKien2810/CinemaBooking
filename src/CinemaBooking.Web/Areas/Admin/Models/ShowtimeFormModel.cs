using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CinemaBooking.Web.Areas.Admin.Models;

/// <summary>
/// Dữ liệu form xếp suất chiếu. Giờ bắt đầu nhập theo giờ rạp (giờ Việt Nam), controller đổi
/// sang UTC trước khi lưu. Giờ kết thúc không nhập — suy ra từ thời lượng phim.
/// </summary>
public sealed class ShowtimeFormModel
{
    public int? Id { get; set; }

    [Display(Name = "Phim")]
    [Range(1, int.MaxValue, ErrorMessage = "Chọn phim.")]
    public int MovieId { get; set; }

    [Display(Name = "Phòng chiếu")]
    [Range(1, int.MaxValue, ErrorMessage = "Chọn phòng chiếu.")]
    public int RoomId { get; set; }

    [Display(Name = "Giờ bắt đầu")]
    [Required(ErrorMessage = "Chọn giờ bắt đầu.")]
    public DateTime? StartTimeLocal { get; set; }

    private decimal _basePrice = 90_000m;

    /// <summary>Gõ theo nghìn đồng (90 → 90.000đ); setter chuẩn hóa nên Range bên dưới kiểm số đồng thật.</summary>
    [Display(Name = "Giá vé (VNĐ)")]
    [Range(typeof(decimal), "10000", "1000000", ErrorMessage = "Giá vé từ 10.000 đến 1.000.000 VNĐ (gõ 90 = 90.000đ).")]
    public decimal BasePrice { get => _basePrice; set => _basePrice = MoneyFormat.ExpandThousands(value); }

    [Display(Name = "Ngôn ngữ")]
    public CinemaBooking.Domain.Enums.ShowLanguage Language { get; set; } = CinemaBooking.Domain.Enums.ShowLanguage.Original;

    /// <summary>Danh mục phim đang chiếu — chỉ để vẽ dropdown, không bind từ request.</summary>
    [ValidateNever] public List<SelectListItem> Movies { get; set; } = [];

    /// <summary>Phòng của các chi nhánh đang hoạt động, gom nhóm theo chi nhánh.</summary>
    [ValidateNever] public List<SelectListItem> Rooms { get; set; } = [];
}

public sealed record ShowtimeAdminItem(
    int Id, string MovieTitle, string TheaterName, string RoomName,
    DateTime StartLocal, DateTime EndLocal, decimal BasePrice, int BookingCount, bool IsActive = true,
    CinemaBooking.Domain.Enums.ShowLanguage Language = CinemaBooking.Domain.Enums.ShowLanguage.Original,
    CinemaBooking.Domain.Enums.RoomType RoomType = CinemaBooking.Domain.Enums.RoomType.Standard);

public sealed record ShowtimesIndexViewModel(
    DateOnly Date, int? TheaterId, List<SelectListItem> Theaters, List<ShowtimeAdminItem> Items,
    int? MovieId = null, string? Status = null, List<SelectListItem>? Movies = null);
