namespace CinemaBooking.Web.Hubs;

/// <summary>
/// Các sự kiện server đẩy xuống trình duyệt. Tên method chính là tên event JS của Dev C lắng nghe
/// (<c>connection.on("SeatLocked", ...)</c>) — đổi tên ở đây là phá client, nên sửa cả hai phía.
/// </summary>
public interface ISeatClient
{
    /// <summary>Trạng thái giữ ghế hiện tại của suất, gửi riêng cho người vừa join.</summary>
    Task SeatSnapshot(IReadOnlyList<HeldSeat> seats);

    /// <summary>Người chọn đã giữ được ghế; <paramref name="holdSeconds"/> để chạy đồng hồ đếm ngược.</summary>
    Task SeatHeldByMe(int seatId, int holdSeconds);

    /// <summary>Người khác trong cùng suất vừa giữ ghế này.</summary>
    Task SeatLocked(int seatId);

    /// <summary>Các ghế vừa trống trở lại (bỏ chọn, rớt mạng, hết hạn).</summary>
    Task SeatUnlocked(IReadOnlyList<int> seatIds);

    /// <summary>
    /// Ghế ĐÃ ĐẶT vừa trống lại (rạp hủy vé, đơn chờ thanh toán quá hạn). Tách khỏi <see cref="SeatUnlocked"/>:
    /// client bỏ qua SeatUnlocked với ghế đã đặt để lock nhả chậm không làm ghế vừa bán hiện trống.
    /// </summary>
    Task SeatFreed(IReadOnlyList<int> seatIds);

    /// <summary>Các ghế vừa được đặt xong (đơn đã Confirmed) — client khác vẽ lại thành "đã đặt".</summary>
    Task SeatBooked(IReadOnlyList<int> seatIds);

    /// <summary>Không giữ được ghế; <paramref name="reason"/> hiển thị thẳng lên toast.</summary>
    Task SeatRejected(int seatId, string reason);
}

/// <summary>
/// Một ghế đang bị giữ. <see cref="Mine"/> = true nếu chính connection nhận snapshot đang giữ;
/// khi đó <see cref="SecondsLeft"/> là số giây còn lại để client chạy tiếp đồng hồ đếm ngược
/// thay vì đếm lại từ đầu. Ghế của người khác không lộ thời hạn (null).
/// </summary>
public sealed record HeldSeat(int SeatId, bool Mine, int? SecondsLeft = null);
