using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
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
    private readonly CinemaDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        CinemaDbContext db,
        TimeProvider clock,
        ILogger<AccountController> logger)
    {
        _users = users;
        _signIn = signIn;
        _db = db;
        _clock = clock;
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

        // Quản lý chi nhánh đăng nhập là vào thẳng khu phòng/ghế của mình; Admin và khách giữ
        // nguyên hành vi cũ (returnUrl hoặc trang chủ).
        if (string.IsNullOrWhiteSpace(returnUrl) && await _users.IsInRoleAsync(user, AppRoles.BranchAdmin))
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });

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

    // ── Quên / đặt lại mật khẩu ─────────────────────────────────────────────

    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    /// <summary>
    /// Luôn trả về cùng một thông báo dù email có tồn tại hay không — tránh lộ danh sách tài khoản.
    /// Link reset được đưa vào outbox để Worker gửi, không gửi SMTP ngay trong request.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _users.FindByEmailAsync(model.Email);
        if (user is not null)
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var link = Url.Action(nameof(ResetPassword), "Account",
                new { email = model.Email, token }, Request.Scheme);

            if (!string.IsNullOrWhiteSpace(link))
            {
                _db.OutboxEmails.Add(new OutboxEmail
                {
                    ToAddress = model.Email,
                    Subject = "Đặt lại mật khẩu CinemaBooking",
                    Body =
                        $"Xin chào {user.FullName ?? user.Email},\n\n" +
                        "Bạn (hoặc ai đó) đã yêu cầu đặt lại mật khẩu cho tài khoản này.\n" +
                        $"Mở liên kết sau để đặt mật khẩu mới (hết hạn sau 24 giờ):\n{link}\n\n" +
                        "Nếu bạn không yêu cầu, hãy bỏ qua email này — mật khẩu hiện tại vẫn giữ nguyên.",
                    Status = OutboxStatus.Pending,
                    CreatedUtc = _clock.GetUtcNow().UtcDateTime,
                });
                await _db.SaveChangesAsync();
            }

            _logger.LogInformation("Đã tạo yêu cầu đặt lại mật khẩu cho {Email}", model.Email);
        }

        ViewData["Sent"] = true;
        return View(new ForgotPasswordViewModel { Email = model.Email });
    }

    [HttpGet]
    public IActionResult ResetPassword(string? email, string? token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return RedirectToAction(nameof(Login));

        return View(new ResetPasswordViewModel { Email = email, Token = token });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _users.FindByEmailAsync(model.Email);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty,
                "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
            return View(model);
        }

        var result = await _users.ResetPasswordAsync(user, model.Token, model.Password);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Success"] = "Đã đặt lại mật khẩu. Vui lòng đăng nhập bằng mật khẩu mới.";
        return RedirectToAction(nameof(Login));
    }

    // ── Trang tài khoản ──────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();

        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        return View(new ProfileViewModel
        {
            FullName = user.FullName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();

        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        // Email là định danh đăng nhập, không cho sửa ở đây.
        model.Email = user.Email ?? string.Empty;
        if (!ModelState.IsValid) return View(model);

        user.FullName = model.FullName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber)
            ? null
            : model.PhoneNumber.Trim();

        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Success"] = "Đã cập nhật thông tin tài khoản.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    public IActionResult ChangePassword()
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        if (!ModelState.IsValid) return View(model);

        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        var result = await _users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        // Đổi mật khẩu làm hết hạn security stamp; refresh cookie để không bị đăng xuất ngay.
        await _signIn.RefreshSignInAsync(user);
        TempData["Success"] = "Đã đổi mật khẩu thành công.";
        return RedirectToAction(nameof(Profile));
    }

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
        "PasswordMismatch" => "Mật khẩu hiện tại không đúng.",
        "InvalidToken" => "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.",
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
