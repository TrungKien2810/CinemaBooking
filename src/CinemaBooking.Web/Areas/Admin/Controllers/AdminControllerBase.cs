using Microsoft.AspNetCore.Mvc;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>Gốc chung của mọi controller trong khu quản trị: gắn area một chỗ, không lặp ở từng controller.</summary>
[Area("Admin")]
public abstract class AdminControllerBase : Controller
{
}
