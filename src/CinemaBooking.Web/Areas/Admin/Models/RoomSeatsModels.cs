using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Web.Areas.Admin.Models;

/// <summary>Trang "Sửa loại ghế" của một phòng: admin chọn Thường / VIP cho từng ghế và ghép hai
/// ghế liền kề cùng hàng thành ghế đôi ở bất kỳ hàng nào.</summary>
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

/// <summary>Một hàng trong trang sửa ghế.</summary>
public sealed record RoomSeatRowViewModel(
    string Label,
    IReadOnlyList<RoomSeatCellViewModel> Seats);

/// <summary><see cref="Locked"/>: ghế đang có vé của suất chưa chiếu — không đổi loại được (giá vé đã bán không đổi).</summary>
public sealed record RoomSeatCellViewModel(int SeatId, string Label, SeatType Type, int Row, int Column, bool Locked = false);
