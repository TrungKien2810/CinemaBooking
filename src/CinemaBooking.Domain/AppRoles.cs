namespace CinemaBooking.Domain;

/// <summary>
/// Tên vai trò dùng chung cho seeder và cho [Authorize(Roles = ...)] ở tầng Web.
/// Gõ chuỗi thẳng trong attribute rất dễ sai chính tả mà build vẫn xanh, lỗi chỉ lộ ra
/// lúc chạy dưới dạng "bị chặn truy cập" — nên gom về hằng số ở đây.
/// <para>
/// Ba vai trò:
/// <list type="bullet">
/// <item><see cref="Admin"/> — quản trị toàn hệ thống: phim, chi nhánh, phòng, suất chiếu, đơn, doanh thu.</item>
/// <item><see cref="BranchAdmin"/> — quản lý MỘT chi nhánh (gắn qua <c>ApplicationUser.TheaterId</c>):
/// chỉ sửa phòng/ghế và xếp suất chiếu của chi nhánh đó.</item>
/// <item><see cref="Customer"/> — khách đặt vé.</item>
/// </list>
/// </para>
/// </summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string BranchAdmin = "BranchAdmin";
    public const string Customer = "Customer";

    /// <summary>Dùng cho attribute: <c>[Authorize(Roles = AppRoles.AdminOrBranchAdmin)]</c>.</summary>
    public const string AdminOrBranchAdmin = Admin + "," + BranchAdmin;

    public static readonly string[] All = [Admin, BranchAdmin, Customer];
}
