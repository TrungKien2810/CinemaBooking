namespace CinemaBooking.Domain.Entities;

/// <summary>
/// Hạn giữ ghế của một khách trong một suất chiếu — lưu MỘT lần cho cặp (UserId, ShowtimeId), không lưu
/// trên từng ghế. Bỏ chọn hết rồi chọn lại vẫn dùng đúng hạn này, nên không gia hạn được bằng cách chọn lại.
/// Dòng chỉ bị xóa khi hết hạn hoặc khi các ghế đã chuyển thành đơn.
/// </summary>
public class SeatHoldSession
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int ShowtimeId { get; set; }
    public DateTime ExpiresUtc { get; set; }
}

/// <summary>
/// Một ghế đang được giữ cho <see cref="UserId"/>. Hạn giữ lấy từ <see cref="SeatHoldSession"/> cùng
/// (UserId, ShowtimeId); unique (ShowtimeId, SeatId) bảo đảm một ghế chỉ có một người giữ.
/// </summary>
public class SeatHold
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int ShowtimeId { get; set; }
    public int SeatId { get; set; }
}
