using CinemaBooking.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace CinemaBooking.Worker.SeatRelease;

/// <summary>
/// Bản mặc định chỉ ghi log, dùng khi Worker không cấu hình được URL của Web. Ghế VẪN được nhả
/// trong <c>SeatLockManager</c>; chỉ là các trình duyệt đang mở suất đó không được báo lại ngay mà
/// phải chờ lần join/snapshot kế tiếp.
/// </summary>
public sealed class LoggingSeatReleaseNotifier : ISeatReleaseNotifier
{
    private readonly ILogger<LoggingSeatReleaseNotifier> _log;

    public LoggingSeatReleaseNotifier(ILogger<LoggingSeatReleaseNotifier> log) => _log = log;

    public Task NotifyAsync(int showtimeId, IReadOnlyList<int> seatIds, CancellationToken ct = default)
    {
        _log.LogInformation("Ghế trống trở lại ở suất {ShowtimeId}: {Seats}", showtimeId, string.Join(",", seatIds));
        return Task.CompletedTask;
    }
}
