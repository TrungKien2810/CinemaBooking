using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CinemaBooking.Web.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        ILogger<AccountController> logger)
    {
        _users = users;
        _signIn = signIn;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        // UserName = Email: hệ thống không có khái niệm nickname, đăng nhập luôn bằng email.
        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
        };

        var result = await _users.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        // Mọi người đăng ký qua form đều là khách. Tài khoản Admin chỉ do IdentitySeeder tạo.
        // Không chặn đăng ký nếu bước này hỏng — tài khoản đã tạo xong rồi, nhưng phải ghi log
        // vì thiếu vai trò là lỗi âm thầm: chỉ lộ ra khi Sprint 4 lọc "vé của tôi" theo vai trò.
        var roleResult = await _users.AddToRoleAsync(user, AppRoles.Customer);
        if (!roleResult.Succeeded)
        {
            _logger.LogError("Không gán được vai trò {Role} cho {Email}: {Errors}",
                AppRoles.Customer, model.Email,
                string.Join("; ", roleResult.Errors.Select(e => e.Description)));
        }

        _logger.LogInformation("Tài khoản mới đã được tạo: {Email}", model.Email);
        await _signIn.SignInAsync(user, isPersistent: false);
        return RedirectToLocal(returnUrl);
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        // Tra user trước rồi mới đăng nhập: UserName trùng Email nên hai cách tương đương,
        // nhưng làm vậy thì đổi quy ước UserName sau này không phá màn đăng nhập.
        var user = await _users.FindByEmailAsync(model.Email);
        if (user is null)
        {
            // Không nói rõ "email không tồn tại" — tránh để người ngoài dò xem ai có tài khoản.
            ModelState.AddModelError(string.Empty, "Email hoặc mật khẩu không đúng.");
            return View(model);
        }

        var result = await _signIn.PasswordSignInAsync(
            user, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty,
                "Tài khoản tạm khóa do nhập sai quá nhiều lần. Vui lòng thử lại sau 5 phút.");
            return View(model);
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Email hoặc mật khẩu không đúng.");
            return View(model);
        }

        return RedirectToLocal(returnUrl);
    }

    /// <summary>
    /// POST chứ không phải GET: link đăng xuất dạng GET có thể bị nhúng vào thẻ img trên
    /// trang khác để đá người dùng ra ngoài. Không gắn [Authorize] vì [AllowAnonymous] ở
    /// cấp class sẽ vô hiệu nó, và SignOutAsync khi chưa đăng nhập vốn là no-op.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(string? returnUrl = null)
    {
        await _signIn.SignOutAsync();
        return RedirectToLocal(returnUrl);
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    /// <summary>
    /// Distinct vì Identity trả cả DuplicateUserName lẫn DuplicateEmail khi đăng ký trùng
    /// (UserName = Email nên một lỗi hóa hai) — sau khi Việt hóa thì hai dòng y hệt nhau.
    /// </summary>
    private void AddErrors(IdentityResult result)
    {
        foreach (var message in result.Errors.Select(Translate).Distinct())
        {
            ModelState.AddModelError(string.Empty, message);
        }
    }

    /// <summary>Việt hóa vài mã lỗi Identity hay gặp; mã lạ thì giữ nguyên mô tả gốc.</summary>
    private static string Translate(IdentityError error) => error.Code switch
    {
        "DuplicateUserName" or "DuplicateEmail" => "Email này đã được đăng ký.",
        "PasswordTooShort" => "Mật khẩu phải từ 8 ký tự trở lên.",
        "PasswordRequiresDigit" => "Mật khẩu phải có ít nhất một chữ số.",
        "PasswordRequiresUpper" => "Mật khẩu phải có ít nhất một chữ in hoa.",
        "PasswordRequiresLower" => "Mật khẩu phải có ít nhất một chữ thường.",
        _ => error.Description,
    };

    /// <summary>
    /// Chỉ nhận đường dẫn nội bộ. Nếu không sẽ dính open redirect: kẻ tấn công gửi link
    /// /Account/Login?returnUrl=https://site-gia và nạn nhân bị đẩy sang đó sau khi đăng nhập.
    /// </summary>
    private IActionResult RedirectToLocal(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl!)
            : RedirectToAction(nameof(HomeController.Index), "Home");
}
