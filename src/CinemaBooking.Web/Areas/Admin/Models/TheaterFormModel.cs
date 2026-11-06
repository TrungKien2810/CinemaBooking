using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Entities;

namespace CinemaBooking.Web.Areas.Admin.Models;

/// <summary>Dữ liệu form thêm/sửa chi nhánh (<see cref="Theater"/>).</summary>
public sealed class TheaterFormModel
{
    public int? Id { get; set; }

    [Display(Name = "Tên chi nhánh")]
    [Required(ErrorMessage = "Nhập tên chi nhánh.")]
    [StringLength(200, ErrorMessage = "Tên chi nhánh tối đa 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Thành phố")]
    [Required(ErrorMessage = "Nhập thành phố.")]
    [StringLength(100, ErrorMessage = "Thành phố tối đa 100 ký tự.")]
    public string City { get; set; } = string.Empty;

    [Display(Name = "Địa chỉ")]
    [StringLength(500, ErrorMessage = "Địa chỉ tối đa 500 ký tự.")]
    public string? Address { get; set; }

    [Display(Name = "Số điện thoại / hotline")]
    [StringLength(30, ErrorMessage = "Số điện thoại tối đa 30 ký tự.")]
    [RegularExpression(@"^[0-9+()\-.\s]*$", ErrorMessage = "Số điện thoại chỉ gồm chữ số, dấu cách và + ( ) - .")]
    public string? Phone { get; set; }

    [Display(Name = "Email liên hệ")]
    [StringLength(256, ErrorMessage = "Email tối đa 256 ký tự.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string? Email { get; set; }

    [Display(Name = "Giờ mở cửa")]
    [StringLength(100, ErrorMessage = "Giờ mở cửa tối đa 100 ký tự.")]
    public string? OpeningHours { get; set; }

    [Display(Name = "Giới thiệu")]
    [StringLength(1000, ErrorMessage = "Giới thiệu tối đa 1000 ký tự.")]
    public string? Description { get; set; }

    [Display(Name = "Đang hoạt động")]
    public bool IsActive { get; set; } = true;

    public static TheaterFormModel From(Theater t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        City = t.City,
        Address = t.Address,
        Phone = t.Phone,
        Email = t.Email,
        OpeningHours = t.OpeningHours,
        Description = t.Description,
        IsActive = t.IsActive,
    };

    public void ApplyTo(Theater t)
    {
        t.Name = Name.Trim();
        t.City = City.Trim();
        t.Address = string.IsNullOrWhiteSpace(Address) ? null : Address.Trim();
        t.Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim();
        t.Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim();
        t.OpeningHours = string.IsNullOrWhiteSpace(OpeningHours) ? null : OpeningHours.Trim();
        t.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        t.IsActive = IsActive;
    }
}

/// <summary>Một dòng của trang danh sách chi nhánh.</summary>
public sealed record TheaterListItem(
    int Id, string Name, string City, string? Address, bool IsActive, int RoomCount,
    string? Phone = null, string? Manager = null);

/// <summary>Danh sách chi nhánh đã lọc + bộ lọc đang chọn (tìm theo tên/địa chỉ, thành phố, trạng thái).</summary>
public sealed record TheatersIndexViewModel(
    List<TheaterListItem> Items, string? Query, string? City, string? Status, IReadOnlyList<string> Cities, int Total)
{
    public bool IsFiltered => Query is not null || City is not null || Status is not null;
}
