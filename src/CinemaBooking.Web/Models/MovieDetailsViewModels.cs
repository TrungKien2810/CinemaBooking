namespace CinemaBooking.Web.Models;

/// <summary>
/// Trang chi tiết phim: thông tin phim + suất chiếu của MỘT ngày, gom theo chi nhánh.
/// Khách chọn phim trước rồi mới chọn nơi xem, nên cùng một phim hiện đủ mọi chi nhánh có chiếu.
/// </summary>
public sealed record MovieDetailsViewModel(
    MovieCardViewModel Movie,
    string? Description,
    IReadOnlyList<DateOnly> Dates,
    DateOnly? SelectedDate,
    IReadOnlyList<BranchShowtimesViewModel> Branches)
{
    public bool HasShowtimes => Branches.Count > 0;
}

public sealed record BranchShowtimesViewModel(
    string TheaterName,
    IReadOnlyList<ShowtimeSlotViewModel> Showtimes);

/// <summary>Một nút giờ chiếu. Giờ đã đổi sang giờ rạp — view không tự cộng offset.</summary>
public sealed record ShowtimeSlotViewModel(
    int Id,
    string RoomName,
    DateTime StartLocal,
    DateTime EndLocal,
    decimal BasePrice);
