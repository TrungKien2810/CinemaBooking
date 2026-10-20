namespace CinemaBooking.Web.Services;

/// <summary>
/// Tiêu chí lọc suất chiếu do khách chọn. <c>null</c> nghĩa là "tất cả". Chi nhánh chỉ là tiêu chí
/// lọc, không phải ranh giới bảo mật (General.md §7.7).
/// </summary>
public sealed record ShowtimeQueryFilter(string? City = null, int? TheaterId = null)
{
    public static readonly ShowtimeQueryFilter None = new();
}

/// <summary>Một chi nhánh đang chiếu phim, để dựng dropdown thành phố / chi nhánh.</summary>
public sealed record ShowtimeBranchOption(int TheaterId, string Name, string City);
