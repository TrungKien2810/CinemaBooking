namespace CinemaBooking.Domain.Enums;

public enum SeatType { Standard = 0, Vip = 1, Couple = 2 }

public enum BookingStatus { Pending = 0, Confirmed = 1, Cancelled = 2, Expired = 3 }

// Giữ nguyên giá trị cũ (Refunded = 3) để dữ liệu đang có không bị đổi nghĩa khi thêm trạng thái mới.
public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Refunded = 3,
    RefundPending = 4,
    RefundFailed = 5,
}

public enum OutboxStatus { Pending = 0, Sent = 1, Failed = 2 }
