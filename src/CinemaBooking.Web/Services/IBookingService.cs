using CinemaBooking.Domain.Abstractions;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Yêu cầu đặt vé. Danh sách ghế lấy từ Session phía server, KHÔNG nhận từ payload client —
/// form có thể bị sửa để gửi ghế bất kỳ (General.md §6.2).
/// <see cref="ConnectionId"/> là owner token của lock: connection SignalR khi còn ở trang
/// chọn ghế, hoặc hold-token trong Session sau khi đã chuyển sang trang thanh toán.
/// </summary>
public sealed record BookingRequest(
    int ShowtimeId,
    IReadOnlyList<int> SeatIds,
    string ConnectionId,
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone,
    string? UserId,
    string? VoucherCode = null);

public enum BookingResultKind
{
    /// <summary>Đã ghi Booking và trả mã tra cứu.</summary>
    Success,
    /// <summary>Ghế không thuộc phòng, đã có người đặt, hoặc chưa chọn ghế.</summary>
    SeatsUnavailable,
    /// <summary>Lock RAM đã hết hạn (khách điền form quá lâu) — phải chọn lại.</summary>
    LocksExpired,
    /// <summary>Suất đã bắt đầu / phim hoặc chi nhánh đã ẩn.</summary>
    ShowtimeNotBookable,
    /// <summary>Mã giảm giá không dùng được (hết hạn, hết lượt, sai chi nhánh...) — khách chọn lại hoặc bỏ mã.</summary>
    VoucherInvalid,
    /// <summary>Cổng thanh toán từ chối — đơn không được xác nhận.</summary>
    PaymentFailed,
    /// <summary>
    /// Đã ghi Booking + giữ ghế (Pending) và tạo URL thanh toán VNPAY — khách cần chuyển hướng sang
    /// cổng thanh toán để hoàn tất. Đơn chỉ được xác nhận khi VNPAY trả về <c>ReturnUrl</c>/IPN.
    /// </summary>
    PaymentRedirected,
}

public sealed record BookingResult(BookingResultKind Kind, string? BookingCode, string? Message, string? PaymentUrl = null)
{
    public bool IsSuccess => Kind == BookingResultKind.Success;

    public bool IsRedirectToPayment => Kind == BookingResultKind.PaymentRedirected;

    public static BookingResult Success(string bookingCode) => new(BookingResultKind.Success, bookingCode, null);

    public static BookingResult Failed(BookingResultKind kind, string message) => new(kind, null, message);

    public static BookingResult RedirectToPayment(string bookingCode, string paymentUrl) =>
        new(BookingResultKind.PaymentRedirected, bookingCode, null, paymentUrl);
}

/// <summary>
/// Luồng đặt vé phía server: kiểm tra lại ghế + lock, ghi Booking/BookingSeats/Payment trong một
/// transaction, thu tiền và xếp email xác nhận vào outbox.
/// </summary>
public interface IBookingService
{
    Task<BookingResult> PlaceAsync(BookingRequest request, CancellationToken ct = default);

    /// <summary>
    /// Xác nhận / hủy đơn sau khi VNPAY xác thực xong kết quả thanh toán (qua ReturnUrl hoặc IPN).
    /// </summary>
    Task<BookingResult> ConfirmAsync(string bookingCode, VnpayVerifyResult verify, CancellationToken ct = default);

    /// <summary>Đưa lại email xác nhận vào outbox (khách làm mất email / không nhận được).</summary>
    Task<BookingResult> ResendConfirmationAsync(string bookingCode, CancellationToken ct = default);
}
