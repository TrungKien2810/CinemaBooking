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
    string? UserId);

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
    /// <summary>Cổng thanh toán từ chối — đơn không được xác nhận.</summary>
    PaymentFailed,
}

public sealed record BookingResult(BookingResultKind Kind, string? BookingCode, string? Message)
{
    public bool IsSuccess => Kind == BookingResultKind.Success;

    public static BookingResult Success(string bookingCode) => new(BookingResultKind.Success, bookingCode, null);

    public static BookingResult Failed(BookingResultKind kind, string message) => new(kind, null, message);
}

/// <summary>
/// Luồng đặt vé phía server: kiểm tra lại ghế + lock, ghi Booking/BookingSeats/Payment trong một
/// transaction, thu tiền và xếp email xác nhận vào outbox.
/// </summary>
public interface IBookingService
{
    Task<BookingResult> PlaceAsync(BookingRequest request, CancellationToken ct = default);
}
