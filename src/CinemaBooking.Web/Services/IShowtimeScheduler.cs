using CinemaBooking.Domain.Entities;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Điền các trường dẫn xuất của một suất chiếu trước khi lưu. Admin (Dev A) gọi service này
/// thay vì tự cộng giờ, để chỉ có một nơi duy nhất biết công thức tính giờ kết thúc.
/// </summary>
public interface IShowtimeScheduler
{
    /// <summary>
    /// Chuẩn hoá <see cref="Showtime.StartTimeUtc"/> về UTC và tính
    /// <see cref="Showtime.EndTimeUtc"/> = giờ bắt đầu + thời lượng phim.
    /// </summary>
    /// <exception cref="ShowtimeSchedulingException">Không tìm thấy phim.</exception>
    Task PrepareAsync(Showtime showtime, CancellationToken ct = default);
}

/// <summary>Lỗi nghiệp vụ khi xếp lịch — controller bắt để trả về lỗi validation, không phải 500.</summary>
public sealed class ShowtimeSchedulingException : Exception
{
    public ShowtimeSchedulingException(string message) : base(message) { }
}
