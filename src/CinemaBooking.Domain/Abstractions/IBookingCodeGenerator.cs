namespace CinemaBooking.Domain.Abstractions;

/// <summary>
/// Sinh mã tra cứu vé mà khách phải đọc qua điện thoại hoặc gõ lại vào trang tra cứu.
/// Mã KHÔNG được đoán được: biết mã là xem được thông tin đơn.
/// </summary>
public interface IBookingCodeGenerator
{
    /// <summary>Một mã mới, dạng <c>CB-XXXXXX</c>, tối đa 20 ký tự theo <c>Booking.BookingCode</c>.</summary>
    string Next();
}
