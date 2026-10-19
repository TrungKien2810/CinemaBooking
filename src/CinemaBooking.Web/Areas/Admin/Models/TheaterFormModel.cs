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

    [Display(Name = "Đang hoạt động")]
    public bool IsActive { get; set; } = true;

    public static TheaterFormModel From(Theater t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        City = t.City,
        Address = t.Address,
        IsActive = t.IsActive,
    };

    public void ApplyTo(Theater t)
    {
        t.Name = Name.Trim();
        t.City = City.Trim();
        t.Address = string.IsNullOrWhiteSpace(Address) ? null : Address.Trim();
        t.IsActive = IsActive;
    }
}

/// <summary>Một dòng của trang danh sách chi nhánh.</summary>
public sealed record TheaterListItem(int Id, string Name, string City, string? Address, bool IsActive, int RoomCount);
