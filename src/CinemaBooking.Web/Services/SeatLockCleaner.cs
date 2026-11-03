using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Infrastructure;
using CinemaBooking.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Quét lock hết hạn mỗi <see cref="SeatHoldOptions.CleanupInterval"/> và phát <c>SeatUnlocked</c>
/// cho group của suất chiếu.
/// <para>
/// Phải chạy TRONG process Web: <see cref="ISeatLockManager"/> là singleton in-memory, mỗi process
/// giữ một bảng lock riêng. Bản cũ chạy ở Worker nên bảng lock bên đó luôn rỗng — ghế hết hạn chỉ
/// được "nhả" về mặt logic (bị <c>TryLock</c> cướp lại) và không tab nào được báo, ghế cứ hiện
/// "đang giữ" cho tới khi khách F5.
/// </para>
/// <para>
/// Gom ghế hết hạn theo suất rồi phát MỘT tín hiệu cho mỗi suất: nhiều tab cùng lúc vẫn chỉ nhận
/// một lần cập nhật thay vì mỗi ghế một tin.
/// </para>
/// </summary>
public sealed class SeatLockCleaner : BackgroundService
{
    private readonly ISeatLockManager _locks;
    private readonly IHubContext<SeatHub, ISeatClient> _hub;
    private readonly SeatHoldOptions _hold;
    private readonly ILogger<SeatLockCleaner> _log;

    public SeatLockCleaner(
        ISeatLockManager locks,
        IHubContext<SeatHub, ISeatClient> hub,
        IOptions<SeatHoldOptions> hold,
        ILogger<SeatLockCleaner> log)
    {
        _locks = locks;
        _hub = hub;
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

    /// <summary>
    /// Quét một lần: nhả mọi lock đã hết hạn rồi báo cho các tab đang mở suất đó.
    /// internal để test gọi trực tiếp, không phải chờ đồng hồ của vòng lặp.
    /// </summary>
    internal async Task CleanOnceAsync(CancellationToken ct = default)
    {
        var byShowtime = _locks.RemoveExpiredLocks(DateTime.UtcNow)
            .GroupBy(key => key.ShowtimeId);

        foreach (var showtime in byShowtime)
        {
            var seatIds = showtime.Select(key => key.SeatId).OrderBy(id => id).ToList();
            await _hub.Clients.Group(SeatHub.GroupName(showtime.Key)).SeatUnlocked(seatIds);
            _log.LogInformation("Đã giải phóng {Count} ghế hết hạn ở suất {ShowtimeId}.", seatIds.Count, showtime.Key);
        }
    }
}
