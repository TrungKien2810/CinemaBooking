using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;

namespace CinemaBooking.Web.Areas.Admin.Models;

/// <summary>Dữ liệu form thêm/sửa phòng chiếu thuộc một chi nhánh.</summary>
public sealed class RoomFormModel : IValidatableObject
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

    [Display(Name = "Loại phòng")]
    public RoomType RoomType { get; set; } = RoomType.Standard;

    [Display(Name = "Mẫu sơ đồ ghế")]
    public SeatLayoutPreset LayoutPreset { get; set; } = SeatLayoutPreset.Auto;

    /// <summary>
    /// 0 = không có lối đi; n = chừa cột n + 1 làm lối đi giữa phòng (cột đó không có ghế). Mỗi bên lối đi phải còn ít nhất
    /// 2 ghế nên n nằm trong [2, số cột − 3] và phòng cần ít nhất 5 cột.
    /// </summary>
    [Display(Name = "Lối đi dọc sau cột số")]
    public int AisleAfterColumn { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (AisleAfterColumn == 0) yield break;

        if (AisleAfterColumn < 0)
        {
            yield return new ValidationResult("Số cột lối đi không được âm (nhập 0 nếu không có lối đi).", [nameof(AisleAfterColumn)]);
        }
        else if (SeatGenerator.AisleRange(TotalColumns) is not { } range)
        {
            yield return new ValidationResult(
                $"Phòng cần ít nhất {SeatGenerator.MinColumnsForAisle} ghế mỗi hàng mới chừa được lối đi (hiện {TotalColumns}).",
                [nameof(AisleAfterColumn)]);
        }
        else if (AisleAfterColumn < range.Min || AisleAfterColumn > range.Max)
        {
            yield return new ValidationResult(
                $"Lối đi phải nằm sau cột {range.Min} đến cột {range.Max} (mỗi bên còn ít nhất {SeatGenerator.MinSeatsPerSide} ghế).",
                [nameof(AisleAfterColumn)]);
        }
    }

    /// <summary>Chỉ dùng khi sửa phòng: sinh lại toàn bộ sơ đồ ghế theo mẫu đã chọn dù không đổi số hàng/cột.</summary>
    [Display(Name = "Sinh lại sơ đồ ghế theo mẫu đã chọn")]
    public bool RegenerateSeats { get; set; }

    public static RoomFormModel From(Room r) => new()
    {
        Id = r.Id,
        TheaterId = r.TheaterId,
        TheaterName = r.Theater?.Name ?? string.Empty,
        RoomName = r.RoomName,
        TotalRows = r.TotalRows,
        TotalColumns = r.TotalColumns,
        RoomType = r.RoomType,
    };
}

public sealed record RoomListItem(
    int Id, string RoomName, int TotalRows, int TotalColumns, int SeatCount, int ShowtimeCount,
    RoomType RoomType = RoomType.Standard);

public sealed record RoomsIndexViewModel(int TheaterId, string TheaterName, List<RoomListItem> Rooms);

