using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Entities;

namespace CinemaBooking.Web.Areas.Admin.Models;

/// <summary>Dữ liệu form thêm/sửa phòng chiếu thuộc một chi nhánh.</summary>
public sealed class RoomFormModel
{
    public int? Id { get; set; }

    public int TheaterId { get; set; }

    /// <summary>Chỉ để hiển thị tiêu đề form, không đọc từ request.</summary>
    public string TheaterName { get; set; } = string.Empty;

    [Display(Name = "Tên phòng")]
    [Required(ErrorMessage = "Nhập tên phòng.")]
    [StringLength(100, ErrorMessage = "Tên phòng tối đa 100 ký tự.")]
    public string RoomName { get; set; } = string.Empty;

    /// <summary>Tối đa 26 hàng vì nhãn ghế dùng một chữ cái A–Z (<see cref="Seat.Label"/>).</summary>
    [Display(Name = "Số hàng")]
    [Range(1, 26, ErrorMessage = "Số hàng từ 1 đến 26 (A–Z).")]
    public int TotalRows { get; set; } = 8;

    [Display(Name = "Số ghế mỗi hàng")]
    [Range(1, 50, ErrorMessage = "Số ghế mỗi hàng từ 1 đến 50.")]
    public int TotalColumns { get; set; } = 12;

    public static RoomFormModel From(Room r) => new()
    {
        Id = r.Id,
        TheaterId = r.TheaterId,
        TheaterName = r.Theater?.Name ?? string.Empty,
        RoomName = r.RoomName,
        TotalRows = r.TotalRows,
        TotalColumns = r.TotalColumns,
    };
}

public sealed record RoomListItem(int Id, string RoomName, int TotalRows, int TotalColumns, int SeatCount, int ShowtimeCount);

public sealed record RoomsIndexViewModel(int TheaterId, string TheaterName, List<RoomListItem> Rooms);
