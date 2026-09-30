using CinemaBooking.Domain.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace CinemaBooking.Web.Hubs;

/// <summary>
/// Mỗi suất chiếu là một SignalR group ("showtime-{id}"): tin nhắn giữ/nhả ghế chỉ đi tới những
/// người đang mở cùng suất, không phát cho cả hệ thống. Client join group khi mở trang chọn ghế,
/// SignalR tự gỡ khỏi mọi group khi connection đóng.
/// </summary>
public class SeatHub : Hub<ISeatClient>
{
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(5);

    private readonly ISeatLockManager _locks;

    public SeatHub(ISeatLockManager locks) => _locks = locks;

    public static string GroupName(int showtimeId) => $"showtime-{showtimeId}";

    public async Task JoinShowtime(int showtimeId)
    {
        if (showtimeId <= 0) throw new HubException("Suất chiếu không hợp lệ.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(showtimeId));

        // Gửi trạng thái hiện tại để client vẽ đúng ngay khi vào (ghế đang giữ).
        // Ghế ĐÃ ĐẶT lấy từ DB ở Controller và render sẵn vào trang, không đi qua hub.
        var held = _locks.GetLocksForShowtime(showtimeId)
            .Select(l => new HeldSeat(l.Key.SeatId, l.ConnectionId == Context.ConnectionId))
            .ToList();
        await Clients.Caller.SeatSnapshot(held);
    }

    /// <summary>Rời trang chọn ghế mà vẫn giữ kết nối (SPA/back) — không nhận tin của suất này nữa.</summary>
    public Task LeaveShowtime(int showtimeId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(showtimeId));

    public async Task SelectSeat(int showtimeId, int seatId)
    {
        var key = new SeatKey(showtimeId, seatId);
        // TODO(Sprint 3): kiểm tra ghế đã Confirmed trong DB trước khi cấp lock.
        var result = _locks.TryLock(key, Context.ConnectionId, Context.UserIdentifier, HoldDuration);

        if (result is LockResult.HeldByOther or LockResult.AlreadyBooked)
        {
            await Clients.Caller.SeatRejected(seatId, "Ghế đang được người khác chọn.");
            return;
        }

        await Clients.Caller.SeatHeldByMe(seatId, (int)HoldDuration.TotalSeconds);
        await Clients.OthersInGroup(GroupName(showtimeId)).SeatLocked(seatId);
    }

    public async Task ReleaseSeat(int showtimeId, int seatId)
    {
        if (!_locks.Release(new SeatKey(showtimeId, seatId), Context.ConnectionId)) return;
        await Clients.Group(GroupName(showtimeId)).SeatUnlocked(new[] { seatId });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Rớt mạng thì trả ghế ngay. Lưu ý: giai đoạn thanh toán KHÔNG được dựa vào
        // connection này — booking đã Pending giữ chỗ bằng bản ghi DB, không bằng lock RAM.
        foreach (var key in _locks.ReleaseAllFor(Context.ConnectionId))
        {
            await Clients.Group(GroupName(key.ShowtimeId)).SeatUnlocked(new[] { key.SeatId });
        }
        await base.OnDisconnectedAsync(exception);
    }
}
