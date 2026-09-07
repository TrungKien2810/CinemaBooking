using Microsoft.AspNetCore.Identity;

namespace CinemaBooking.Domain.Entities;

/// <summary>
/// Dùng ASP.NET Identity: Email/PasswordHash/PhoneNumber đã có sẵn trong IdentityUser,
/// vai trò quản lý qua IdentityRole ("Admin", "Customer") — không tự dựng cột Role.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
