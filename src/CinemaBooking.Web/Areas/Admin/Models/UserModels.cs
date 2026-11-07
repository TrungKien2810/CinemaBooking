using System.ComponentModel.DataAnnotations;
using CinemaBooking.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace CinemaBooking.Web.Areas.Admin.Models;

public sealed record UserListItem(
    string Id, string Email, string? FullName, string? Phone, string Role, string? TheaterName, bool IsLocked, bool IsSelf);

public sealed record UsersIndexViewModel(
    List<UserListItem> Items, string? Query, string? Role, PageInfo Paging, int CustomerCount, int StaffCount);

/// <summary>Form tạo tài khoản quản lý chi nhánh / sửa thông tin tài khoản.</summary>
public sealed class UserFormModel
{
    public string? Id { get; set; }

    [Display(Name = "Email đăng nhập")]
    [Required(ErrorMessage = "Nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Họ tên")]
    [Required(ErrorMessage = "Nhập họ tên.")]
    [StringLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Số điện thoại")]
    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    [StringLength(20)]
    public string? PhoneNumber { get; set; }

    [Display(Name = "Chi nhánh quản lý")]
    [Required(ErrorMessage = "Chọn chi nhánh mà tài khoản này quản lý.")]
    public int? TheaterId { get; set; }

    /// <summary>Bắt buộc khi tạo mới; khi sửa để trống = không đổi mật khẩu.</summary>
    [Display(Name = "Mật khẩu")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu tối thiểu 8 ký tự.")]
    public string? Password { get; set; }

    /// <summary>Vai trò hiện tại (chỉ để hiển thị khi sửa); tạo mới luôn là BranchAdmin.</summary>
    [ValidateNever] public string Role { get; set; } = "BranchAdmin";

    [ValidateNever] public List<SelectListItem> Theaters { get; set; } = [];
}
