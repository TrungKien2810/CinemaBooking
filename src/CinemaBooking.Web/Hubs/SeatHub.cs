using CinemaBooking.Domain.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace CinemaBooking.Web.Hubs;

/// <summary>
/// Mỗi suất chiếu là một SignalR group ("showtime-{id}").
/// Client join group khi mở trang chọn ghế, rời group khi disconnect.
/// </summary>
public class SeatHub : Hub
{
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(5);

    private readonly ISeatLockManager _locks;

    public SeatHub(ISeatLockManager locks) => _locks = locks;

    public static string GroupName(int showtimeId) => $"showtime-{showtimeId}";

    public async Task JoinShowtime(int showtimeId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(showtimeId));

        // Gửi trạng thái hiện tại để client vẽ đúng ngay khi vào (ghế đang giữ).
        // Ghế ĐÃ ĐẶT lấy từ DB ở Controller và render sẵn vào trang, không đi qua hub.
        var held = _locks.GetLocksForShowtime(showtimeId)
            .Select(l => new { seatId = l.Key.SeatId, mine = l.ConnectionId == Context.ConnectionId })
            .ToList();
        await Clients.Caller.SendAsync("SeatSnapshot", held);
    }

    public async Task SelectSeat(int showtimeId, int seatId)
    {
        var key = new SeatKey(showtimeId, seatId);
        // TODO(Sprint 3): kiểm tra ghế đã Confirmed trong DB trước khi cấp lock.
        var result = _locks.TryLock(key, Context.ConnectionId, Context.UserIdentifier, HoldDuration);

        if (result is LockResult.HeldByOther or LockResult.AlreadyBooked)
        {
            await Clients.Caller.SendAsync("SeatRejected", seatId, "Ghế đang được người khác chọn.");
            return;
        }

        await Clients.Caller.SendAsync("SeatHeldByMe", seatId, HoldDuration.TotalSeconds);
        await Clients.OthersInGroup(GroupName(showtimeId)).SendAsync("SeatLocked", seatId);
    }

    public async Task ReleaseSeat(int showtimeId, int seatId)
    {
        if (!_locks.Release(new SeatKey(showtimeId, seatId), Context.ConnectionId)) return;
        await Clients.Group(GroupName(showtimeId)).SendAsync("SeatUnlocked", new[] { seatId });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Rớt mạng thì trả ghế ngay. Lưu ý: giai đoạn thanh toán KHÔNG được dựa vào
        // connection này — booking đã Pending giữ chỗ bằng bản ghi DB, không bằng lock RAM.
        foreach (var key in _locks.ReleaseAllFor(Context.ConnectionId))
        {
            await Clients.Group(GroupName(key.ShowtimeId))
                .SendAsync("SeatUnlocked", new[] { key.SeatId });
        }
        await base.OnDisconnectedAsync(exception);
    }
}
