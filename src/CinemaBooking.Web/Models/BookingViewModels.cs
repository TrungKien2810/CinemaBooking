using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Web.Models;

/// <summary>Một dòng ghế trên vé đã đặt — giá lấy từ <c>BookingSeat.PriceAtBooking</c>.</summary>
public sealed record BookingSeatLine(
    string Label, SeatType Type, decimal Price, decimal Discount = 0m, string? PromotionTitle = null)
{
    public bool HasPromotion => Discount > 0 && !string.IsNullOrEmpty(PromotionTitle);
}

/// <summary>Thông tin đầy đủ của một vé, dùng cho trang xác nhận, tra cứu và danh sách vé.</summary>
public sealed record BookingTicketViewModel(
    string BookingCode,
    string MovieTitle,
    string TheaterName,
    string City,
    string RoomName,
    DateTime StartLocal,
    DateTime BookingTimeLocal,
    BookingStatus Status,
    PaymentStatus PaymentState,
    string PaymentMethod,
    string? TransactionId,
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone,
    decimal TotalAmount,
    IReadOnlyList<BookingSeatLine> Seats,
    bool IsMasked = false,
    DateTime? CheckedInLocal = null,
    string? CheckedInBy = null,
    string? FormatLabel = null)
{
    /// <summary>Vé đã được nhân viên soát tại rạp.</summary>
    public bool IsCheckedIn => CheckedInLocal is not null;

    public string StatusLabel => Status switch
    {
        BookingStatus.Confirmed => "Đã xác nhận",
        BookingStatus.Pending => "Đang chờ thanh toán",
        BookingStatus.Cancelled => "Đã hủy",
        BookingStatus.Expired => "Đã hết hạn",
        _ => Status.ToString(),
    };

    public string PaymentStateLabel => PaymentState switch
    {
        PaymentStatus.Succeeded => "Đã thanh toán",
        PaymentStatus.Pending => "Đang chờ",
        PaymentStatus.Failed => "Thất bại",
        PaymentStatus.Refunded => "Đã hoàn tiền",
        PaymentStatus.RefundPending => "Đang hoàn tiền",
        PaymentStatus.RefundFailed => "Hoàn tiền thất bại",
        _ => PaymentState.ToString(),
    };
}

/// <summary>Trang tra cứu vé theo mã: <see cref="Code"/> là mã khách vừa nhập, <see cref="Booking"/> null nếu không thấy.</summary>
public sealed record BookingLookupViewModel(string? Code, string? Error, BookingTicketViewModel? Booking);

/// <summary>Danh sách vé của tài khoản đang đăng nhập, mới nhất trước.</summary>
public sealed record MyBookingsViewModel(
    IReadOnlyList<BookingTicketViewModel> Bookings,
    string Tab = "all",
    int UpcomingCount = 0,
    int HistoryCount = 0);
