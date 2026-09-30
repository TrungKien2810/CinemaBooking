using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Infrastructure;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Hubs;

/// <summary>
/// Mỗi suất chiếu là một SignalR group ("showtime-{id}"): tin nhắn giữ/nhả ghế chỉ đi tới những
/// người đang mở cùng suất, không phát cho cả hệ thống. Client join group khi mở trang chọn ghế,
/// SignalR tự gỡ khỏi mọi group khi connection đóng.
/// </summary>
public class SeatHub : Hub<ISeatClient>
{
    /// <summary>Hạn mức ghế một connection giữ cùng lúc trong một suất — chặn một người khóa cả phòng.</summary>
    public const int MaxSeatsPerConnection = 8;

    private const string JoinedKey = "SeatHub.JoinedShowtimes";

    private readonly ISeatLockManager _locks;
    private readonly SeatHoldOptions _hold;
    private readonly ISeatAvailabilityQuery _availability;

    // Hub sống theo từng invocation trong scope riêng, nên inject service Scoped (DbContext) là an toàn.
    public SeatHub(ISeatLockManager locks, IOptions<SeatHoldOptions> hold, ISeatAvailabilityQuery availability)
    {
        _locks = locks;
        _hold = hold.Value;
        _availability = availability;
    }

    /// <summary>Thời gian giữ lấy từ section SeatHold — Worker dọn lock theo cùng cấu hình.</summary>
    private int HoldSeconds => (int)_hold.Duration.TotalSeconds;

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

        // Vào group TRƯỚC rồi mới chụp snapshot: một SeatLocked xảy ra giữa hai bước vẫn tới được
        // client (có thể trùng với snapshot — vô hại). Làm ngược lại thì event đó rơi mất và ghế
        // hiện trống trên máy này cho tới khi có event kế tiếp.
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(showtimeId));
        JoinedShowtimes.Add(showtimeId);

        // Ghế ĐÃ ĐẶT lấy từ DB ở Controller và render sẵn vào trang, không đi qua hub.
        await Clients.Caller.SeatSnapshot(Snapshot(showtimeId, DateTime.UtcNow));
    }

    private List<HeldSeat> Snapshot(int showtimeId, DateTime nowUtc) =>
        _locks.GetLocksForShowtime(showtimeId)
            .OrderBy(l => l.Key.SeatId)
            .Select(l => l.ConnectionId == Context.ConnectionId
                ? new HeldSeat(l.Key.SeatId, true, SecondsUntil(l.ExpiresUtc, nowUtc))
                : new HeldSeat(l.Key.SeatId, false))
            .ToList();

    private static int SecondsUntil(DateTime expiresUtc, DateTime nowUtc) =>
        Math.Max(0, (int)Math.Ceiling((expiresUtc - nowUtc).TotalSeconds));

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

        // Hỏi DB sau các kiểm tra rẻ ở trên. Đây không phải chốt chặn cuối — hai người vẫn có
        // thể lọt qua cùng lúc — chốt cuối là unique index (ShowtimeId, SeatId) lúc ghi Booking.
        switch (await _availability.CheckAsync(showtimeId, seatId, Context.ConnectionAborted))
        {
            case SeatAvailability.Booked:
                await Clients.Caller.SeatRejected(seatId, "Ghế đã có người đặt.");
                return;
            case SeatAvailability.NotBookable:
                await Clients.Caller.SeatRejected(seatId, "Ghế hoặc suất chiếu không còn mở bán.");
                return;
        }

        var result = _locks.TryLock(key, Context.ConnectionId, Context.UserIdentifier, _hold.Duration);

        switch (result)
        {
            case LockResult.Acquired:
                await Clients.Caller.SeatHeldByMe(seatId, HoldSeconds);
                // OthersInGroup chứ không phải Group: người chọn đã tô ghế "của mình" qua
                // SeatHeldByMe, nhận thêm SeatLocked sẽ vẽ đè thành "người khác giữ".
                // Kể cả khi lock vừa được cướp từ người giữ đã hết hạn, người đó cũng nằm trong
                // "others" nên thấy ghế chuyển sang bị giữ.
                await Clients.OthersInGroup(GroupName(showtimeId)).SeatLocked(seatId);
                break;

            case LockResult.AlreadyHeldBySelf:
                // Lock đã được gia hạn; báo lại để client khởi động lại đồng hồ đếm ngược.
                await Clients.Caller.SeatHeldByMe(seatId, HoldSeconds);
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
        // Gom theo suất: đóng tab đang giữ 6 ghế là 1 tin SeatUnlocked cho group, không phải 6.
        var releasedByShowtime = _locks.ReleaseAllFor(Context.ConnectionId)
            .GroupBy(k => k.ShowtimeId);

        foreach (var showtime in releasedByShowtime)
        {
            var seatIds = showtime.Select(k => k.SeatId).OrderBy(id => id).ToList();
            await Clients.Group(GroupName(showtime.Key)).SeatUnlocked(seatIds);
        }

        // Group membership do SignalR tự gỡ khi connection đóng; chỉ cần dọn state của mình.
        Context.Items.Remove(JoinedKey);
        await base.OnDisconnectedAsync(exception);
    }
}
