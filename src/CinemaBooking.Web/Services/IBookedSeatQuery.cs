namespace CinemaBooking.Web.Services;

/// <summary>
/// Ghế ĐÃ ĐẶT của một suất chiếu, đọc từ DB. Trang chọn ghế dùng để vẽ ghế đỏ; SeatHub
/// dùng để từ chối cấp lock cho ghế đã bán (B3.8).
/// </summary>
public interface IBookedSeatQuery
{
    /// <summary>
    /// Mọi ghế đang có dòng <c>BookingSeats</c> của suất này. Không lọc theo trạng thái đơn:
    /// unique index (ShowtimeId, SeatId) chặn theo đúng tiêu chí này, nên ghế nào DB sẽ từ chối
    /// thì phải hiện là đã đặt — kể cả đơn Pending đang thanh toán. Hủy vé phải xóa dòng
    /// BookingSeats để trả ghế (A4.3).
    /// </summary>
    Task<IReadOnlySet<int>> GetBookedSeatIdsAsync(int showtimeId, CancellationToken ct = default);
}
