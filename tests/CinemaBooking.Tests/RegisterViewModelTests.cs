using System.ComponentModel.DataAnnotations;
using CinemaBooking.Web.Models;

namespace CinemaBooking.Tests;

/// <summary>
/// Chặn lại đúng lỗi đã gặp: thiếu ErrorMessage trên một attribute thì thông báo rơi về
/// tiếng Anh mặc định của .NET giữa một form toàn tiếng Việt, mà build vẫn xanh.
/// </summary>
public class RegisterViewModelTests
{
    private static List<ValidationResult> Validate(RegisterViewModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static RegisterViewModel Valid() => new()
    {
        FullName = "Nguyễn Văn A",
        Email = "khach@example.com",
        Password = "Matkhau@123",
        ConfirmPassword = "Matkhau@123",
    };

    [Fact]
    public void A_fully_filled_form_passes()
    {
        Assert.Empty(Validate(Valid()));
    }

    [Fact]
    public void Every_required_field_reports_in_Vietnamese()
    {
        var messages = Validate(new RegisterViewModel()).Select(r => r.ErrorMessage).ToList();

        Assert.Contains("Vui lòng nhập họ tên.", messages);
        Assert.Contains("Vui lòng nhập email.", messages);
        Assert.Contains("Vui lòng nhập mật khẩu.", messages);
        // [Compare] không bao hàm [Required]. Gỡ [Required] ra thì dòng này mất hẳn ở đây,
        // còn trong pipeline MVC thật nó hóa thành thông báo tiếng Anh mặc định.
        Assert.Contains("Vui lòng nhập lại mật khẩu.", messages);
    }

    [Fact]
    public void Mismatched_confirmation_is_rejected()
    {
        var model = Valid();
        model.ConfirmPassword = "MatkhauKhac@1";

        Assert.Contains("Mật khẩu nhập lại không khớp.", Validate(model).Select(r => r.ErrorMessage));
    }

    [Fact]
    public void Password_shorter_than_the_identity_minimum_is_rejected()
    {
        var model = Valid();
        model.Password = model.ConfirmPassword = "Ngan@12";

        Assert.Contains("Mật khẩu phải từ 8 ký tự trở lên.", Validate(model).Select(r => r.ErrorMessage));
    }

    [Fact]
    public void No_validation_message_falls_back_to_the_English_default()
    {
        var tooLong = new RegisterViewModel
        {
            FullName = new string('a', 201),
            Email = new string('a', 250) + "@example.com",
            Password = "x",
            ConfirmPassword = "y",
        };

        var messages = Validate(new RegisterViewModel()).Concat(Validate(tooLong))
            .Select(r => r.ErrorMessage ?? string.Empty)
            .ToList();

        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.DoesNotContain("The ", m));
        Assert.All(messages, m => Assert.DoesNotContain("field", m));
    }
}
