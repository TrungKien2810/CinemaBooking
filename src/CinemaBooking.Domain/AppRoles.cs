namespace CinemaBooking.Domain;

/// <summary>
/// Tên vai trò dùng chung cho seeder và cho [Authorize(Roles = ...)] ở tầng Web.
/// Gõ chuỗi thẳng trong attribute rất dễ sai chính tả mà build vẫn xanh, lỗi chỉ lộ ra
/// lúc chạy dưới dạng "bị chặn truy cập" — nên gom về hằng số ở đây.
/// </summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";

    public static readonly string[] All = [Admin, Customer];
}
