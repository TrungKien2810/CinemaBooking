namespace CinemaBooking.Web.Services;

/// <summary>Cấu hình chính sách hủy vé phía khách.</summary>
public sealed class BookingCancellationOptions
{
    public const string SectionName = "BookingCancellation";

    /// <summary>Số giờ tối thiểu trước giờ chiếu để khách được tự hủy.</summary>
    public int MinHoursBeforeShowtime { get; set; } = 2;

    /// <summary>Cho phép khách guest hủy bằng mã vé + email đã dùng khi đặt.</summary>
    public bool AllowGuestCancel { get; set; } = true;
}
