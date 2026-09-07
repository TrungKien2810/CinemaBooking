using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Domain.Entities;

/// <summary>
/// Hàng đợi email bền vững. Web chỉ GHI vào bảng này trong cùng transaction với Booking;
/// Worker mới là nơi thật sự gửi. Tránh fire-and-forget mất email khi web process chết.
/// </summary>
public class OutboxEmail
{
    public int Id { get; set; }
    [Required, MaxLength(256)] public string ToAddress { get; set; } = string.Empty;
    [Required, MaxLength(300)] public string Subject { get; set; } = string.Empty;
    [Required] public string Body { get; set; } = string.Empty;
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int AttemptCount { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? SentUtc { get; set; }
}

/// <summary>Báo cáo doanh thu cuối ngày do Worker tổng hợp.</summary>
public class DailyRevenueReport
{
    public int Id { get; set; }
    public DateOnly ReportDate { get; set; }
    public int MovieId { get; set; }
    public Movie? Movie { get; set; }
    public int TicketsSold { get; set; }
    public decimal Revenue { get; set; }
    public DateTime GeneratedUtc { get; set; }
}
