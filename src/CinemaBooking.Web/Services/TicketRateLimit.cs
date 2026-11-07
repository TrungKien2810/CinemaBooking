namespace CinemaBooking.Web.Services;

/// <summary>Giới hạn tần suất cho các lối công khai nhận mã vé.</summary>
public static class TicketRateLimit
{
    public const string Policy = "ticket-lookup";
    public const int PerMinute = 30;
}
