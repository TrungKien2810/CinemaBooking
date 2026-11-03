using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
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
    private readonly IHubContext<SeatHub, ISeatClient> _hub;
    private readonly VnpayOptions _vnpay;
    private readonly TimeProvider _clock;
    private readonly ILogger<ExpiredBookingCleaner> _log;

    public ExpiredBookingCleaner(
        IServiceScopeFactory scopes,
        IHubContext<SeatHub, ISeatClient> hub,
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

        var stale = await db.Bookings
            .Include(b => b.Seats)
            .Include(b => b.Payment)
            .Where(b => b.Status == BookingStatus.Pending && b.BookingTimeUtc <= cutoff)
            .OrderBy(b => b.BookingTimeUtc) // đơn cũ nhất trước: Take(200) phải xác định, không phụ thuộc thứ tự DB trả về
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
            await _hub.Clients.Group(SeatHub.GroupName(showtime.ShowtimeId)).SeatUnlocked(showtime.SeatIds);

        _log.LogInformation("Đã hết hạn {Count} đơn chờ thanh toán và trả ghế.", stale.Count);
    }
}
