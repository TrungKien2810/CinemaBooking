namespace CinemaBooking.Web.Services;

public enum SeatAvailability
{
    /// <summary>Suất còn bán, ghế thuộc phòng của suất và chưa có ai đặt.</summary>
    Available,
    /// <summary>Suất không tồn tại / đã bắt đầu / phim hoặc chi nhánh đã ẩn, hoặc ghế không thuộc phòng.</summary>
    NotBookable,
    /// <summary>Ghế đã có dòng BookingSeats cho suất này — unique index sẽ từ chối, nên không cấp lock.</summary>
    Booked,
}

/// <summary>
/// Kiểm tra ở DB trước khi SeatHub cấp lock RAM. Payload hub do client gửi nên
/// (showtimeId, seatId) có thể là bất kỳ — không kiểm thì một script giữ được ghế ma hoặc ghế đã bán.
/// </summary>
public interface ISeatAvailabilityQuery
{
    Task<SeatAvailability> CheckAsync(int showtimeId, int seatId, CancellationToken ct = default);
}
