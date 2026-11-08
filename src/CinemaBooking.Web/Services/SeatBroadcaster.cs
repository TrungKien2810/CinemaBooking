using CinemaBooking.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Chỗ DUY NHẤT ngoài <see cref="SeatHub"/> phát sự kiện ghế tới group của một suất chiếu. Đổi tên/payload sự kiện
/// (khớp <c>seat-map.js</c>) chỉ sửa ở đây. Hub tự phát qua <c>Clients</c> của nó vì cần loại trừ caller.
/// </summary>
public sealed class SeatBroadcaster
{
    private readonly IHubContext<SeatHub, ISeatClient> _hub;

    public SeatBroadcaster(IHubContext<SeatHub, ISeatClient> hub) => _hub = hub;

    /// <summary>Ghế vừa chuyển sang đã đặt / đang thanh toán.</summary>
    public Task BookedAsync(int showtimeId, IReadOnlyList<int> seatIds) =>
        _hub.Clients.Group(SeatHub.GroupName(showtimeId)).SeatBooked(seatIds);

    /// <summary>Ghế đã đặt / đang thanh toán vừa trống lại.</summary>
    public Task FreedAsync(int showtimeId, IReadOnlyList<int> seatIds) =>
        _hub.Clients.Group(SeatHub.GroupName(showtimeId)).SeatFreed(seatIds);

    /// <summary>Lượt giữ ghế hết hạn / bị nhả.</summary>
    public Task UnlockedAsync(int showtimeId, IReadOnlyList<int> seatIds) =>
        _hub.Clients.Group(SeatHub.GroupName(showtimeId)).SeatUnlocked(seatIds);
}
