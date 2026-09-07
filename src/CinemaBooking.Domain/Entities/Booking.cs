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
    public decimal PriceAtBooking { get; set; }
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
}
