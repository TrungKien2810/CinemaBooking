using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Web.Models;

/// <summary>Tóm tắt giỏ ghế đang lưu trong Session trước khi khách điền thông tin và thanh toán.</summary>
public sealed record CheckoutViewModel(
    int ShowtimeId,
    string MovieTitle,
    string TheaterName,
    string City,
    string RoomName,
    DateTime StartLocal,
    IReadOnlyList<CheckoutSeatItem> Seats,
    decimal TotalAmount,
    string? FormatLabel = null);

/// <summary><see cref="Price"/> là giá khách trả (đã trừ khuyến mãi); <see cref="ListPrice"/> là giá niêm yết.</summary>
public sealed record CheckoutSeatItem(
    string Label, SeatType Type, decimal Price, decimal ListPrice = 0m, string? PromotionTitle = null)
{
    public bool HasPromotion => PromotionTitle is not null && ListPrice > Price;
}

/// <summary>
/// Trang thanh toán: tóm tắt đơn (đọc từ Session) + form thông tin khách.
/// <see cref="HoldSecondsRemaining"/> để view chạy đồng hồ đếm ngược — ghế chỉ được giữ 5 phút kể
/// từ lúc bấm "Tiếp tục thanh toán", khách ngồi lâu quá thì phải quay lại chọn ghế.
/// </summary>
public sealed record CheckoutPageViewModel(
    CheckoutViewModel Summary,
    CheckoutFormModel Form,
    int HoldSecondsRemaining = 0);

/// <summary>
/// Thông tin liên hệ của khách trên form thanh toán. Danh sách ghế KHÔNG nằm ở đây —
/// server đọc lại từ Session, không tin payload client (General.md §6.2).
/// </summary>
public sealed class CheckoutFormModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
    [StringLength(200)]
    [Display(Name = "Họ tên")]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string CustomerEmail { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    [StringLength(20)]
    [Display(Name = "Số điện thoại")]
    public string? CustomerPhone { get; set; }
}
