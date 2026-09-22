namespace CinemaBooking.Web.Models;

/// <summary>Dữ liệu trang chọn ghế của một suất chiếu. Giờ đã đổi sang giờ rạp.</summary>
public sealed record SeatMapViewModel(
    int ShowtimeId,
    string MovieTitle,
    string TheaterName,
    string City,
    string RoomName,
    DateTime StartLocal,
    int HoldSeconds);
