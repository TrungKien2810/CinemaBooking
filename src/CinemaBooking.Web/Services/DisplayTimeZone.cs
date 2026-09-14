namespace CinemaBooking.Web.Services;

/// <summary>
/// Lệch giờ giữa UTC (cách DB lưu) và giờ rạp (cách khách nhìn lịch chiếu).
/// Dùng offset cố định thay vì <see cref="TimeZoneInfo"/> theo tên để kết quả giống nhau trên
/// máy dev Windows và runner Linux của CI — Việt Nam không có DST nên không mất gì.
/// </summary>
public sealed class DisplayTimeZone
{
    public const string SectionName = "Display";

    /// <summary>Mặc định UTC+7 (giờ Việt Nam).</summary>
    public int TimeZoneOffsetHours { get; set; } = 7;

    public TimeSpan Offset => TimeSpan.FromHours(TimeZoneOffsetHours);

    public DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(utc + Offset);

    public DateTime ToLocalTime(DateTime utc) => utc + Offset;

    /// <summary>Nửa khoảng [từ, đến) theo UTC ứng với trọn một ngày ở giờ rạp.</summary>
    public (DateTime FromUtc, DateTime ToUtc) DayRangeUtc(DateOnly localDate)
    {
        var startUtc = localDate.ToDateTime(TimeOnly.MinValue) - Offset;
        return (startUtc, startUtc.AddDays(1));
    }
}
