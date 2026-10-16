using CinemaBooking.Domain.Abstractions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Worker.SeatRelease;

/// <summary>
/// Phát SeatUnlocked ra process Web qua một HubConnection — Worker không giữ <c>IHubContext</c> nên
/// phải nói chuyện với SeatHub như một client bình thường. Kết nối mở lười ở lần phát đầu tiên và
/// tự hồi phục nhờ <c>WithAutomaticReconnect</c>; Web chết thì ghế vẫn được nhả trong
/// <c>SeatLockManager</c>, chỉ là trình duyệt chưa được báo ngay.
/// </summary>
public sealed class SignalRSeatReleaseNotifier : ISeatReleaseNotifier, IAsyncDisposable
{
    public const string HubPath = "/hubs/seat";
    public const string Method = "NotifySeatsReleased";

    private readonly HubConnection _connection;
    private readonly ILogger<SignalRSeatReleaseNotifier> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public SignalRSeatReleaseNotifier(IOptions<WebHubOptions> options, ILogger<SignalRSeatReleaseNotifier> log)
    {
        _log = log;
        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(options.Value.BaseUrl), HubPath))
            .WithAutomaticReconnect()
            .Build();
    }

    public async Task NotifyAsync(int showtimeId, IReadOnlyList<int> seatIds, CancellationToken ct = default)
    {
        if (seatIds.Count == 0) return;

        try
        {
            await EnsureConnectedAsync(ct);
            await _connection.InvokeAsync(Method, showtimeId, seatIds.ToArray(), ct);
        }
        catch (Exception ex)
        {
            // Không phát được không phải lỗi chí mạng: ghế đã trống trong RAM, client sẽ đồng bộ lại
            // ở lần join kế tiếp. Nuốt lỗi để vòng quét của ExpiredSeatCleaner chạy tiếp.
            _log.LogWarning(ex, "Không phát được SeatUnlocked cho suất {ShowtimeId}.", showtimeId);
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_connection.State == HubConnectionState.Connected) return;

        await _gate.WaitAsync(ct);
        try
        {
            if (_connection.State == HubConnectionState.Disconnected)
                await _connection.StartAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
