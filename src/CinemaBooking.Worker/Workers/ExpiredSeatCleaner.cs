using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Infrastructure;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Worker.Workers;

/// <summary>
/// Quét lock hết hạn mỗi CleanupIntervalSeconds (mặc định 10s — KHÔNG phải 60s; 1 phút để ghế treo
/// quá lâu sau khi hết hạn). Gom ghế hết hạn theo suất chiếu rồi phát MỘT tín hiệu SeatUnlocked cho
/// mỗi suất qua <see cref="ISeatReleaseNotifier"/> — đóng nhiều tab cùng lúc cũng không làm client
/// phải vẽ lại từng ghế một.
/// </summary>
public sealed class ExpiredSeatCleaner : BackgroundService
{
    private readonly ISeatLockManager _locks;
    private readonly ISeatReleaseNotifier _notifier;
    private readonly SeatHoldOptions _hold;
    private readonly ILogger<ExpiredSeatCleaner> _log;

    public ExpiredSeatCleaner(
        ISeatLockManager locks,
        ISeatReleaseNotifier notifier,
        IOptions<SeatHoldOptions> hold,
        ILogger<ExpiredSeatCleaner> log)
    {
        _locks = locks;
        _notifier = notifier;
        _hold = hold.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_hold.CleanupInterval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await CleanOnceAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Lỗi khi dọn ghế hết hạn — vòng lặp vẫn tiếp tục.");
            }
        }
    }

    private async Task CleanOnceAsync(CancellationToken ct)
    {
        var byShowtime = _locks.RemoveExpiredLocks(DateTime.UtcNow)
            .GroupBy(key => key.ShowtimeId);

        foreach (var showtime in byShowtime)
        {
            var seatIds = showtime.Select(key => key.SeatId).OrderBy(id => id).ToList();
            await _notifier.NotifyAsync(showtime.Key, seatIds, ct);
            _log.LogInformation("Đã giải phóng {Count} ghế hết hạn ở suất {ShowtimeId}.", seatIds.Count, showtime.Key);
        }
    }
}
