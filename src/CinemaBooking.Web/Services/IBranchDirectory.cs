namespace CinemaBooking.Web.Services;

/// <summary>Danh sách chi nhánh đang hoạt động cho phía khách: bộ lọc thành phố và trang chi nhánh.</summary>
public interface IBranchDirectory
{
    /// <summary>Chi nhánh đang mở, sắp theo thành phố rồi tên.</summary>
    Task<IReadOnlyList<BranchListItem>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Chi tiết một chi nhánh đang mở: phòng, phim còn suất sắp chiếu, khuyến mãi còn hiệu lực.
    /// Trả null nếu chi nhánh không tồn tại hoặc đã đóng.
    /// </summary>
    Task<BranchDetails?> GetDetailsAsync(int id, DateTime nowUtc, DateOnly today, CancellationToken ct = default);
}

public sealed record BranchListItem(int Id, string Name, string City, string? Address, int RoomCount);

public sealed record BranchRoomInfo(string Name, int Rows, int Columns, int SeatCount, int VipCount, int CoupleCount);

public sealed record BranchMovieInfo(
    int MovieId, string Title, string? Genre, int DurationMinutes, string? PosterUrl,
    int UpcomingShowtimes, DateTime NextShowtimeUtc, decimal FromPrice);

public sealed record BranchPromotionInfo(
    int Id, string Title, string? Description, string? Badge, DateOnly StartDate, DateOnly EndDate);

public sealed record BranchDetails(
    int Id, string Name, string City, string? Address,
    IReadOnlyList<BranchRoomInfo> Rooms,
    IReadOnlyList<BranchMovieInfo> Movies,
    IReadOnlyList<BranchPromotionInfo> Promotions);
