using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Worker.Workers;

/// <summary>
/// Hết hạn các đơn đặt vé <c>Pending</c> đã quá hạn chờ thanh toán VNPAY và trả ghế.
/// <para>
/// Đơn Pending giữ ghế bằng chính dòng <c>BookingSeats</c> (unique index (ShowtimeId, SeatId) không
/// lọc theo trạng thái). Khách đóng tab ở cổng thanh toán hoặc giao dịch lỗi mà VNPAY không gọi về
/// ReturnUrl/IPN thì đơn nằm Pending mãi — ghế bị khóa dù chưa ai trả tiền. Worker quét định kỳ để
/// trả ghế và báo cho Web phát "ghế trống" tới các tab đang mở.
/// </para>
/// </summary>
public sealed class ExpiredBookingCleaner : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly ISeatReleaseNotifier _notifier;
    private readonly VnpayOptions _vnpay;
    private readonly TimeProvider _clock;
    private readonly ILogger<ExpiredBookingCleaner> _log;

    public ExpiredBookingCleaner(
        IServiceScopeFactory scopes,
        ISeatReleaseNotifier notifier,
        IOptions<VnpayOptions> vnpay,
        TimeProvider clock,
        ILogger<ExpiredBookingCleaner> log)
    {
        _scopes = scopes;
        _notifier = notifier;
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

    private async Task ExpireOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();

        var cutoff = _clock.GetUtcNow().UtcDateTime - _vnpay.PendingHoldWindow;

        var stale = await db.Bookings
            .Include(b => b.Seats)
            .Include(b => b.Payment)
            .Where(b => b.Status == BookingStatus.Pending && b.BookingTimeUtc <= cutoff)
            .Take(200)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        var freed = stale
            .SelectMany(b => b.Seats.Select(s => new { b.ShowtimeId, s.SeatId }))
            .GroupBy(x => x.ShowtimeId)
            .Select(g => new { ShowtimeId = g.Key, SeatIds = g.Select(x => x.SeatId).OrderBy(id => id).ToList() })
            .ToList();

        foreach (var booking in stale)
        {
            db.BookingSeats.RemoveRange(booking.Seats);
            booking.Status = BookingStatus.Expired;
            if (booking.Payment is not null) booking.Payment.Status = PaymentStatus.Failed;
        }

        await db.SaveChangesAsync(ct);

        foreach (var showtime in freed)
            await _notifier.NotifyAsync(showtime.ShowtimeId, showtime.SeatIds, ct);

        _log.LogInformation("Đã hết hạn {Count} đơn chờ thanh toán và trả ghế.", stale.Count);
    }
}
