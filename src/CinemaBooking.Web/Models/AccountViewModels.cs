using System.ComponentModel.DataAnnotations;

namespace CinemaBooking.Web.Models;

public class RegisterViewModel
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
}

public class LoginViewModel
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
