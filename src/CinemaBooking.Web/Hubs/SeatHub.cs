using CinemaBooking.Domain;
using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Web.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CinemaBooking.Web.Hubs;

/// <summary>
/// Mỗi suất chiếu là một SignalR group ("showtime-{id}"): tin SeatLocked/SeatUnlocked/SeatBooked/SeatFreed
/// chỉ đi tới những người đang mở cùng suất. Client join group khi mở trang chọn ghế, SignalR tự gỡ khỏi
/// mọi group khi connection đóng.
/// <para>
/// Chọn ghế = giữ ghế trong DB (<see cref="SeatHoldService"/>) với hạn cố định, không gia hạn; phải đăng
/// nhập, UserId lấy từ claims. Hạn hết thì <see cref="ExpiredBookingCleaner"/> nhả ghế và phát SeatUnlocked.
/// Rớt mạng KHÔNG nhả ghế (giữ theo hạn, không theo connection) nên F5 không mất ghế.
/// </para>
/// </summary>
[Authorize]
public class SeatHub : Hub<ISeatClient>
{
    /// <summary>Hạn mức ghế một khách giữ cùng lúc trong một suất — bằng mức tối đa của một đơn.</summary>
    public const int MaxSeatsPerConnection = BookingService.MaxSeatsPerOrder;

    private const string JoinedKey = "SeatHub.JoinedShowtimes";

    private readonly ISeatAvailabilityQuery _availability;
    private readonly SeatHoldService _holds;

    // Hub sống theo từng invocation trong scope riêng, nên inject service Scoped (DbContext) là an toàn.
    public SeatHub(ISeatAvailabilityQuery availability, SeatHoldService holds)
    {
        _availability = availability;
        _holds = holds;
    }

    private string UserId => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new HubException("Vui lòng đăng nhập để chọn ghế.");

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

        // Ghế ĐÃ ĐẶT / đang thanh toán lấy từ DB ở Controller và render sẵn vào trang; sau đó
        // SeatBooked/SeatFreed cập nhật trực tiếp cho những ai đã vào group.
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(showtimeId));
        JoinedShowtimes.Add(showtimeId);

        var held = await _holds.SnapshotAsync(UserId, showtimeId, Context.ConnectionAborted);
        await Clients.Caller.SeatSnapshot(held.Select(h => new HeldSeat(h.SeatId, h.Mine, h.Mine ? h.SecondsLeft : null)).ToList());
    }

    /// <summary>Rời trang chọn ghế mà vẫn giữ kết nối (SPA/back) — không nhận tin của suất này nữa.</summary>
    public Task LeaveShowtime(int showtimeId)
    {
        JoinedShowtimes.Remove(showtimeId);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(showtimeId));
    }

    // KHÔNG có method cho client tự phát SeatBooked/SeatFreed: ai gọi được cũng sẽ bịa được trạng thái
    // ghế cho người khác. Các event đó chỉ do server phát (BookingController, ExpiredBookingCleaner...).

    /// <summary>
    /// Chọn ghế: ghế phải còn bán được (không nằm trong đơn Pending/Confirmed), rồi giữ ghế cho người gọi.
    /// Người gọi nhận <c>SeatHeldByMe</c> kèm số giây còn lại của hạn cố định; người khác trong suất nhận
    /// <c>SeatLocked</c>. Ai giữ trước được ghế; người sau nhận <c>SeatRejected</c>.
    /// </summary>
    public async Task SelectSeat(int showtimeId, int seatId)
    {
        var userId = UserId;

        // Admin/BranchAdmin chỉ xem sơ đồ; không chọn ghế (kể cả khi tự gọi hub bằng tay).
        if (Context.User?.IsInRole(AppRoles.Admin) == true || Context.User?.IsInRole(AppRoles.BranchAdmin) == true)
        {
            await Clients.Caller.SeatRejected(seatId, "Tài khoản quản trị không đặt vé.");
            return;
        }

        // Chưa join thì client sẽ không nhận tin của suất này — chọn "mù" như vậy chỉ làm sơ đồ trên máy đó lệch.
        if (!JoinedShowtimes.Contains(showtimeId))
        {
            await Clients.Caller.SeatRejected(seatId, "Vui lòng tải lại trang chọn ghế.");
            return;
        }

        switch (await _availability.CheckAsync(showtimeId, seatId, Context.ConnectionAborted))
        {
            case SeatAvailability.Booked:
                await Clients.Caller.SeatRejected(seatId, "Ghế đã có người đặt hoặc đang thanh toán.");
                return;
            case SeatAvailability.NotBookable:
                await Clients.Caller.SeatRejected(seatId, "Ghế hoặc suất chiếu không còn mở bán.");
                return;
        }

        var result = await _holds.SelectAsync(userId, showtimeId, seatId, MaxSeatsPerConnection, Context.ConnectionAborted);
        if (result.Released is { Count: > 0 })
            await Clients.Group(GroupName(showtimeId)).SeatUnlocked(result.Released);

        if (!result.Ok)
        {
            await Clients.Caller.SeatRejected(seatId, result.Reason!);
            return;
        }

        await Clients.Caller.SeatHeldByMe(seatId, result.SecondsLeft);
        if (result.IsNew) await Clients.OthersInGroup(GroupName(showtimeId)).SeatLocked(seatId);
    }

    /// <summary>Bỏ chọn ghế của chính mình (hạn giữ không đổi); báo người khác trong suất để họ chọn được.</summary>
    public async Task ReleaseSeat(int showtimeId, int seatId)
    {
        if (await _holds.ReleaseAsync(UserId, showtimeId, seatId, Context.ConnectionAborted))
            await Clients.Group(GroupName(showtimeId)).SeatUnlocked(new[] { seatId }); // cả tab khác của chính mình
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Group membership do SignalR tự gỡ khi connection đóng; chỉ cần dọn state của mình.
        Context.Items.Remove(JoinedKey);
        await base.OnDisconnectedAsync(exception);
    }
}
