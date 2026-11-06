using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Domain.Entities;

public class Booking
{
    public int Id { get; set; }
    /// <summary>Mã tra cứu hiển thị cho khách (ví dụ CB-8F3A21).</summary>
    [Required, MaxLength(20)] public string BookingCode { get; set; } = string.Empty;
    /// <summary>Null khi khách đặt vé không đăng nhập (guest checkout).</summary>
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public int ShowtimeId { get; set; }
    public Showtime? Showtime { get; set; }
    [Required, MaxLength(200)] public string CustomerName { get; set; } = string.Empty;
    [Required, EmailAddress, MaxLength(256)] public string CustomerEmail { get; set; } = string.Empty;
    [Phone, MaxLength(20)] public string? CustomerPhone { get; set; }
    public DateTime BookingTimeUtc { get; set; }
    public decimal TotalAmount { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    /// <summary>Thời điểm nhân viên soát vé tại rạp; null = chưa vào rạp. Một vé chỉ soát được một lần.</summary>
    public DateTime? CheckedInUtc { get; set; }

    /// <summary>Email tài khoản nhân viên đã soát vé (để truy vết).</summary>
    [MaxLength(256)] public string? CheckedInBy { get; set; }

    public ICollection<BookingSeat> Seats { get; set; } = new List<BookingSeat>();
    public Payment? Payment { get; set; }
}

public class BookingSeat
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public Booking? Booking { get; set; }
    public int SeatId { get; set; }
    public Seat? Seat { get; set; }
    /// <summary>Lặp lại từ Booking để đặt unique index (ShowtimeId, SeatId) — chốt chặn chống đặt trùng ở tầng DB.</summary>
    public int ShowtimeId { get; set; }
    public Showtime? Showtime { get; set; }
    /// <summary>Giá khách thực trả cho ghế này (đã trừ khuyến mãi).</summary>
    public decimal PriceAtBooking { get; set; }

    /// <summary>Số tiền khuyến mãi đã trừ khỏi giá niêm yết của ghế này (0 nếu không có).</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>Tên khuyến mãi đã áp dụng, chụp lại lúc đặt (sửa/xóa khuyến mãi sau này không đổi vé cũ).</summary>
    [MaxLength(150)] public string? PromotionTitle { get; set; }
}

public class Payment
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public Booking? Booking { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(50)] public string PaymentMethod { get; set; } = "Mock";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    [MaxLength(100)] public string? TransactionId { get; set; }
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// <c>vnp_CreateDate</c> gốc đã gửi sang VNPAY (GMT+7, yyyyMMddHHmmss). API hoàn tiền bắt buộc
    /// phải có đúng giá trị này ở <c>vnp_TransactionDate</c>.
    /// </summary>
    [MaxLength(14)] public string? VnpayCreateDate { get; set; }

    /// <summary>Thời điểm VNPAY xác nhận thanh toán (<c>vnp_PayDate</c>), GMT+7.</summary>
    [MaxLength(14)] public string? VnpayPayDate { get; set; }

    public DateTime? PaidUtc { get; set; }
}
