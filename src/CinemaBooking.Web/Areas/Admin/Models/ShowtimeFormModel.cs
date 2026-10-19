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

    [Display(Name = "Giá vé (VNĐ)")]
    [Range(typeof(decimal), "10000", "1000000", ErrorMessage = "Giá vé từ 10.000 đến 1.000.000 VNĐ.")]
    public decimal BasePrice { get; set; } = 90_000m;

    /// <summary>Danh mục phim đang chiếu — chỉ để vẽ dropdown, không bind từ request.</summary>
    [ValidateNever] public List<SelectListItem> Movies { get; set; } = [];

    /// <summary>Phòng của các chi nhánh đang hoạt động, gom nhóm theo chi nhánh.</summary>
    [ValidateNever] public List<SelectListItem> Rooms { get; set; } = [];
}

public sealed record ShowtimeAdminItem(
    int Id, string MovieTitle, string TheaterName, string RoomName,
    DateTime StartLocal, DateTime EndLocal, decimal BasePrice, int BookingCount);

public sealed record ShowtimesIndexViewModel(
    DateOnly Date, int? TheaterId, List<SelectListItem> Theaters, List<ShowtimeAdminItem> Items);
