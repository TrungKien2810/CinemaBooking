using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Web.Models;

/// <summary>Một dòng ghế trên vé đã đặt — giá lấy từ <c>BookingSeat.PriceAtBooking</c>.</summary>
public sealed record BookingSeatLine(
    string Label, SeatType Type, decimal Price, decimal Discount = 0m, string? PromotionTitle = null, decimal VoucherShare = 0m)
{
    public bool HasPromotion => Discount > 0 && !string.IsNullOrEmpty(PromotionTitle);

    /// <summary>Giá niêm yết của ghế = giá đã trả + khuyến mãi + phần mã giảm giá.</summary>
    public decimal ListPrice => Price + Discount + VoucherShare;

    public bool HasAnyDiscount => Discount + VoucherShare > 0;
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
    string? FormatLabel = null,
    string? VoucherCode = null,
    decimal VoucherDiscount = 0m)
{
    /// <summary>Vé đã được nhân viên soát tại rạp.</summary>
    public bool IsCheckedIn => CheckedInLocal is not null;

    public string StatusLabel => Status.Label();

    public string PaymentStateLabel => PaymentState.Label();

    /// <summary>Rạp chủ động hủy vé đã thanh toán (khách không tự hủy được), tiền đang/đã được hoàn.</summary>
    public bool CancelledByCinema =>
        Status == BookingStatus.Cancelled && PaymentState is PaymentStatus.RefundPending or PaymentStatus.Refunded;
}

/// <summary>Trang tra cứu vé theo mã: <see cref="Code"/> là mã khách vừa nhập, <see cref="Booking"/> null nếu không thấy.</summary>
public sealed record BookingLookupViewModel(string? Code, string? Error, BookingTicketViewModel? Booking);

/// <summary>Danh sách vé của tài khoản đang đăng nhập, mới nhất trước.</summary>
public sealed record MyBookingsViewModel(
    IReadOnlyList<BookingTicketViewModel> Bookings,
    string Tab = "all",
    int UpcomingCount = 0,
    int HistoryCount = 0,
    int AllCount = 0,
    PageInfo? Page = null);
