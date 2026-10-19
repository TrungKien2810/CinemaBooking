using CinemaBooking.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>
/// Gốc chung của mọi controller trong khu quản trị: gắn area và phân quyền một chỗ.
/// <see cref="AuthorizeAttribute"/> được kế thừa, nên controller mới chỉ cần kế thừa class này là
/// được bảo vệ — không rải attribute trên từng action rồi quên một cái (General.md §7.7).
/// </summary>
[Area("Admin")]
[Authorize(Roles = AppRoles.Admin)]
public abstract class AdminControllerBase : Controller
{
}
