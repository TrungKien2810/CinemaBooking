namespace CinemaBooking.Web.Services;

/// <summary>Danh sách chi nhánh đang hoạt động cho phía khách: bộ lọc thành phố và trang chi nhánh.</summary>
public interface IBranchDirectory
{
    /// <summary>Chi nhánh đang mở, sắp theo thành phố rồi tên.</summary>
    Task<IReadOnlyList<BranchListItem>> GetActiveAsync(CancellationToken ct = default);
}

public sealed record BranchListItem(int Id, string Name, string City, string? Address, int RoomCount);
