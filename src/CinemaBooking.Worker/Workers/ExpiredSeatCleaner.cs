using CinemaBooking.Domain.Abstractions;
using Microsoft.AspNetCore.SignalR.Client;

namespace CinemaBooking.Worker.Workers;

/// <summary>
/// Quét lock hết hạn mỗi CleanupIntervalSeconds (mặc định 10s — KHÔNG phải 60s;
/// 1 phút để ghế treo quá lâu sau khi hết hạn).
/// Worker chạy ngoài process web nên không gọi thẳng IHubContext được:
/// nó bắn tín hiệu qua SignalR client tới endpoint nội bộ của web.
/// </summary>
public sealed class ExpiredSeatCleaner : BackgroundService
{
    private readonly ISeatLockManager _locks;
    private readonly IConfiguration _config;
    private readonly ILogger<ExpiredSeatCleaner> _log;

    public ExpiredSeatCleaner(ISeatLockManager locks, IConfiguration config, ILogger<ExpiredSeatCleaner> log)
    {
        _locks = locks;
        _config = config;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_config.GetValue("SeatHold:CleanupIntervalSeconds", 10));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var expired = _locks.RemoveExpiredLocks(DateTime.UtcNow);
                if (expired.Count == 0) continue;

                // TODO(Sprint 4): publish SeatUnlocked qua HubConnection tới /hubs/seat.
                _log.LogInformation("Đã giải phóng {Count} ghế hết hạn giữ.", expired.Count);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Lỗi khi dọn ghế hết hạn — vòng lặp vẫn tiếp tục.");
            }
        }
    }
}
