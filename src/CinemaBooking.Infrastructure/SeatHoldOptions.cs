namespace CinemaBooking.Infrastructure;

/// <summary>
/// Section <c>SeatHold</c> trong appsettings, dùng chung cho SeatHub, BookingController và
/// SeatLockCleaner (đều trong process Web) — một nguồn số duy nhất, không hard-code mỗi nơi một kiểu.
/// </summary>
public sealed class SeatHoldOptions
{
    public const string SectionName = "SeatHold";

    /// <summary>Số phút một ghế được giữ kể từ lần chọn (hoặc gia hạn) gần nhất.</summary>
    public int DurationMinutes { get; set; } = 5;

    /// <summary>Chu kỳ quét lock hết hạn. General.md §13 mục 2: 10 giây, không phải 1 phút.</summary>
    public int CleanupIntervalSeconds { get; set; } = 10;

    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);

    public TimeSpan CleanupInterval => TimeSpan.FromSeconds(CleanupIntervalSeconds);
}
