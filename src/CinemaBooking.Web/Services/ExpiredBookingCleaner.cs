using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Hết hạn các đơn đặt vé <c>Pending</c> đã quá hạn chờ thanh toán VNPAY và trả ghế.
/// <para>
/// Đơn Pending giữ ghế bằng chính dòng <c>BookingSeats</c> (unique index (ShowtimeId, SeatId) không
/// lọc theo trạng thái). Khách đóng tab ở cổng thanh toán hoặc giao dịch lỗi mà VNPAY không gọi về
/// ReturnUrl/IPN thì đơn nằm Pending mãi — ghế bị khóa dù chưa ai trả tiền. Vòng quét định kỳ trả ghế
/// và báo cho các tab đang mở suất chiếu vẽ lại ghế ngay.
/// </para>
/// <para>
/// Chạy trong process Web (không phải Worker) để dùng được <see cref="IHubContext{THub, T}"/> — bản cũ
/// ở Worker phải mở HubConnection giả làm client, kéo theo một method công khai ai cũng gọi được.
/// </para>
/// </summary>
public sealed class ExpiredBookingCleaner : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly SeatBroadcaster _hub;
    private readonly VnpayOptions _vnpay;
    private readonly TimeProvider _clock;
    private readonly ILogger<ExpiredBookingCleaner> _log;

    public ExpiredBookingCleaner(
        IServiceScopeFactory scopes,
        SeatBroadcaster hub,
        IOptions<VnpayOptions> vnpay,
        TimeProvider clock,
        ILogger<ExpiredBookingCleaner> log)
    {
        _scopes = scopes;
        _hub = hub;
        _vnpay = vnpay.Value;
        _clock = clock;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(ScanInterval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await ExpireOnceAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Lỗi khi hết hạn đơn chờ thanh toán — vòng lặp vẫn tiếp tục.");
            }
        }
    }

    /// <summary>internal để test gọi trực tiếp, không phải chờ đồng hồ của vòng lặp.</summary>
    internal async Task ExpireOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();

        var cutoff = _clock.GetUtcNow().UtcDateTime - _vnpay.PendingHoldWindow;

        // Lượt giữ ghế hết hạn: nhả ghế và báo cho các tab đang mở suất để họ chọn được ngay (không đợi thao tác kế tiếp).
        var holds = scope.ServiceProvider.GetRequiredService<SeatHoldService>();
        foreach (var released in await holds.SweepExpiredAsync(ct))
            await _hub.UnlockedAsync(released.ShowtimeId, released.SeatIds);

        var freed = await PendingExpiry.ExpireAsync(db, cutoff, null, 200, ct);
        if (freed.Count == 0) return;

        foreach (var showtime in freed)
            await _hub.FreedAsync(showtime.ShowtimeId, showtime.SeatIds);

        _log.LogInformation("Đã hết hạn đơn chờ thanh toán ở {Count} suất chiếu và trả ghế.", freed.Count);
    }
}
