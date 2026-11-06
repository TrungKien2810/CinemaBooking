using System.ComponentModel.DataAnnotations;

namespace CinemaBooking.Web.Models;

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
    [StringLength(200, ErrorMessage = "Họ tên tối đa {1} ký tự.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(256, ErrorMessage = "Email tối đa {1} ký tự.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    // Độ dài tối thiểu phải khớp Identity.Password.RequiredLength trong Program.cs,
    // nếu lệch thì client báo hợp lệ rồi server mới từ chối — trải nghiệm rất tệ.
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải từ {2} ký tự trở lên.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    // [Compare] không bao hàm [Required]; thiếu nó thì MVC tự coi string non-nullable là
    // bắt buộc và bắn ra thông báo tiếng Anh mặc định giữa một form toàn tiếng Việt.
    [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Mật khẩu nhập lại không khớp.")]
    [Display(Name = "Nhập lại mật khẩu")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Kiểm tra ở controller: phải có ở server, ô tick trên trình duyệt có thể bị bỏ qua.</summary>
    public bool AcceptTerms { get; set; }
}

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Ghi nhớ đăng nhập")]
    public bool RememberMe { get; set; }
}

public sealed class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel
{
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải từ {2} ký tự trở lên.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu mới")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu mới.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Mật khẩu nhập lại không khớp.")]
    [Display(Name = "Nhập lại mật khẩu mới")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ProfileViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
    [StringLength(200, ErrorMessage = "Họ tên tối đa {1} ký tự.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [RegularExpression(PhoneRules.Pattern, ErrorMessage = PhoneRules.Message)]
    [StringLength(30, ErrorMessage = "Số điện thoại tối đa {1} ký tự.")]
    [Display(Name = "Số điện thoại")]
    public string? PhoneNumber { get; set; }
}

/// <summary>Một quy tắc số điện thoại cho hồ sơ và thanh toán: [Phone] có sẵn nhận cả chuỗi như "12".</summary>
public static class PhoneRules
{
    public const string Pattern = @"^\+?[0-9][0-9 .\-]{8,14}$";
    public const string Message = "Số điện thoại không hợp lệ.";
}

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu hiện tại")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải từ {2} ký tự trở lên.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu mới")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu mới.")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu nhập lại không khớp.")]
    [Display(Name = "Nhập lại mật khẩu mới")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
