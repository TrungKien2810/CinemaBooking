using Microsoft.AspNetCore.Identity;

namespace CinemaBooking.Domain.Entities;

/// <summary>
/// Dùng ASP.NET Identity: Email/PasswordHash/PhoneNumber đã có sẵn trong IdentityUser,
/// vai trò quản lý qua IdentityRole ("Admin", "BranchAdmin", "Customer") — không tự dựng cột Role.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }

    /// <summary>
    /// Chi nhánh mà tài khoản <c>BranchAdmin</c> được quản lý. Null với Admin toàn hệ thống và khách.
    /// Đây là nguồn duy nhất quyết định phạm vi dữ liệu của branch admin — không tin tham số trên URL.
    /// </summary>
    public int? TheaterId { get; set; }

    public Theater? Theater { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
