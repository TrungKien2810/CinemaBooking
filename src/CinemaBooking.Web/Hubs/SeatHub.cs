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

    /// <summary>Hạn mức ghế một connection giữ cùng lúc trong một suất — chặn một người khóa cả phòng.</summary>
    public const int MaxSeatsPerConnection = 8;

    private const string JoinedKey = "SeatHub.JoinedShowtimes";

    private readonly ISeatLockManager _locks;

    public SeatHub(ISeatLockManager locks) => _locks = locks;

    public static string GroupName(int showtimeId) => $"showtime-{showtimeId}";

    /// <summary>
    /// Các suất connection này đã join. Hub là transient nên phải cất vào Context.Items (sống theo
    /// connection); SignalR chạy tuần tự các invocation của một connection nên HashSet là đủ.
    /// </summary>
    private HashSet<int> JoinedShowtimes =>
        Context.Items.TryGetValue(JoinedKey, out var value) && value is HashSet<int> set
            ? set
            : (HashSet<int>)(Context.Items[JoinedKey] = new HashSet<int>());

    public async Task JoinShowtime(int showtimeId)
    {
        if (showtimeId <= 0) throw new HubException("Suất chiếu không hợp lệ.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(showtimeId));
        JoinedShowtimes.Add(showtimeId);

        // Gửi trạng thái hiện tại để client vẽ đúng ngay khi vào (ghế đang giữ).
        // Ghế ĐÃ ĐẶT lấy từ DB ở Controller và render sẵn vào trang, không đi qua hub.
        var held = _locks.GetLocksForShowtime(showtimeId)
            .Select(l => new HeldSeat(l.Key.SeatId, l.ConnectionId == Context.ConnectionId))
            .ToList();
        await Clients.Caller.SeatSnapshot(held);
    }

    /// <summary>Rời trang chọn ghế mà vẫn giữ kết nối (SPA/back) — không nhận tin của suất này nữa.</summary>
    public Task LeaveShowtime(int showtimeId)
    {
        JoinedShowtimes.Remove(showtimeId);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(showtimeId));
    }

    public async Task SelectSeat(int showtimeId, int seatId)
    {
        // Chưa join thì client sẽ không nhận SeatUnlocked/SeatLocked của suất này — giữ ghế
        // "mù" như vậy chỉ tạo ra ghế treo trên máy người khác.
        if (!JoinedShowtimes.Contains(showtimeId))
        {
            await Clients.Caller.SeatRejected(seatId, "Vui lòng tải lại trang chọn ghế.");
            return;
        }

        var key = new SeatKey(showtimeId, seatId);
        var mine = _locks.GetLocksForShowtime(showtimeId)
            .Where(l => l.ConnectionId == Context.ConnectionId)
            .Select(l => l.Key)
            .ToList();

        // Chọn lại ghế mình đang giữ = gia hạn, không tính thêm vào hạn mức.
        if (!mine.Contains(key) && mine.Count >= MaxSeatsPerConnection)
        {
            await Clients.Caller.SeatRejected(seatId, $"Mỗi lần chỉ giữ tối đa {MaxSeatsPerConnection} ghế.");
            return;
        }

        // TODO(Sprint 3): kiểm tra ghế đã Confirmed trong DB trước khi cấp lock.
        var result = _locks.TryLock(key, Context.ConnectionId, Context.UserIdentifier, HoldDuration);

        switch (result)
        {
            case LockResult.Acquired:
                await Clients.Caller.SeatHeldByMe(seatId, (int)HoldDuration.TotalSeconds);
                // OthersInGroup chứ không phải Group: người chọn đã tô ghế "của mình" qua
                // SeatHeldByMe, nhận thêm SeatLocked sẽ vẽ đè thành "người khác giữ".
                // Kể cả khi lock vừa được cướp từ người giữ đã hết hạn, người đó cũng nằm trong
                // "others" nên thấy ghế chuyển sang bị giữ.
                await Clients.OthersInGroup(GroupName(showtimeId)).SeatLocked(seatId);
                break;

            case LockResult.AlreadyHeldBySelf:
                // Lock đã được gia hạn; báo lại để client khởi động lại đồng hồ đếm ngược.
                await Clients.Caller.SeatHeldByMe(seatId, (int)HoldDuration.TotalSeconds);
                break;

            case LockResult.AlreadyBooked:
                await Clients.Caller.SeatRejected(seatId, "Ghế đã có người đặt.");
                break;

            default:
                await Clients.Caller.SeatRejected(seatId, "Ghế đang được người khác chọn.");
                break;
        }
    }

    public async Task ReleaseSeat(int showtimeId, int seatId)
    {
        var key = new SeatKey(showtimeId, seatId);

        if (_locks.Release(key, Context.ConnectionId))
        {
            // Cả group, kể cả người nhả: một event duy nhất để mọi máy tô lại ghế trống.
            await Clients.Group(GroupName(showtimeId)).SeatUnlocked(new[] { seatId });
            return;
        }

        // Không nhả được = lock không còn là của mình (đã hết hạn và bị dọn, hoặc người khác đã
        // cướp sau khi hết hạn). Chỉ sửa lại màn hình người gọi, không làm phiền cả group.
        var holder = _locks.GetLocksForShowtime(showtimeId).FirstOrDefault(l => l.Key == key);
        if (holder is null)
            await Clients.Caller.SeatUnlocked(new[] { seatId });
        else if (holder.ConnectionId != Context.ConnectionId)
            await Clients.Caller.SeatLocked(seatId);
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
