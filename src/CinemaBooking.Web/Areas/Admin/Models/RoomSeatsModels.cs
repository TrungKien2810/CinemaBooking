using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Web.Areas.Admin.Models;

/// <summary>Trang "Sửa loại ghế" của một phòng: hàng cuối luôn là ghế đôi, các hàng còn lại
/// admin chọn Thường/VIP.</summary>
public sealed record RoomSeatsViewModel(
    int RoomId,
    int TheaterId,
    string RoomName,
    string TheaterName,
    int TotalRows,
    int TotalColumns,
    IReadOnlyList<RoomSeatRowViewModel> Rows)
{
    public int VipCount => Rows.SelectMany(r => r.Seats).Count(s => s.Type == SeatType.Vip);
    public int StandardCount => Rows.SelectMany(r => r.Seats).Count(s => s.Type == SeatType.Standard);
    public int CoupleCount => Rows.SelectMany(r => r.Seats).Count(s => s.Type == SeatType.Couple);
}

/// <summary>Một hàng trong trang sửa ghế. <see cref="IsCoupleRow"/> = hàng cuối (khóa, tự động ghế đôi).</summary>
public sealed record RoomSeatRowViewModel(
    string Label,
    bool IsCoupleRow,
    IReadOnlyList<RoomSeatCellViewModel> Seats);

public sealed record RoomSeatCellViewModel(int SeatId, string Label, SeatType Type);
