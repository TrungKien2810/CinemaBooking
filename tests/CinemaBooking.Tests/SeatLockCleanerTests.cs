using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Services;
using CinemaBooking.Web.Hubs;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

/// <summary>
/// Bản cũ của bộ dọn ghế chạy ở Worker nên bảng lock in-memory bên đó luôn rỗng: ghế không được nhả
/// và không tab nào nhận được SeatUnlocked. Test khóa hành vi ĐÚNG: lock hết hạn bị xóa khỏi manager
/// và mỗi suất chiếu nhận đúng một tín hiệu, lock còn hạn thì không bị đụng.
/// </summary>
public sealed class SeatLockCleanerTests
{
    private const int Showtime = 7;

    [Fact]
    public async Task Removes_expired_locks_and_broadcasts_once_per_showtime()
    {
        var locks = new InMemorySeatLockManager();
        locks.TryLock(new SeatKey(Showtime, 10), "conn-a", null, TimeSpan.FromMinutes(-1));
        locks.TryLock(new SeatKey(Showtime, 11), "conn-a", null, TimeSpan.FromMinutes(-1));
        locks.TryLock(new SeatKey(Showtime, 12), "conn-b", null, TimeSpan.FromMinutes(5));

        var hub = new RecordingHub();
        await Cleaner(locks, hub).CleanOnceAsync();

        var broadcast = Assert.Single(hub.Broadcasts);
        Assert.Equal(SeatHub.GroupName(Showtime), broadcast.Group);
        Assert.Equal(new[] { 10, 11 }, broadcast.SeatIds);

        // Ghế còn hạn của người khác phải nguyên vẹn.
        Assert.True(locks.ValidateLocks(new[] { new SeatKey(Showtime, 12) }, "conn-b"));
        Assert.False(locks.ValidateLocks(new[] { new SeatKey(Showtime, 10) }, "conn-a"));
    }

    /// <summary>
    /// Lỗ hổng cũ: method hub công khai cho phép BẤT KỲ client nào phát "ghế đã trống" cho người
    /// khác. Việc dọn ghế giờ do server tự làm qua IHubContext nên không được còn lối vào nào.
    /// </summary>
    [Fact]
    public void Hub_exposes_no_method_for_clients_to_fake_a_seat_release()
    {
        var method = typeof(SeatHub).GetMethod(
            "NotifySeatsReleased",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.Null(method);
    }

    [Fact]
    public async Task Does_not_broadcast_when_nothing_expired()
    {
        var locks = new InMemorySeatLockManager();
        locks.TryLock(new SeatKey(Showtime, 10), "conn-a", null, TimeSpan.FromMinutes(5));

        var hub = new RecordingHub();
        await Cleaner(locks, hub).CleanOnceAsync();

        Assert.Empty(hub.Broadcasts);
    }

    private static SeatLockCleaner Cleaner(ISeatLockManager locks, RecordingHub hub) =>
        new(locks, hub, Options.Create(new SeatHoldOptions()), NullLogger<SeatLockCleaner>.Instance);

    /// <summary>Ghi lại mọi tin SeatUnlocked, đủ để kiểm tra group + danh sách ghế.</summary>
    private sealed class RecordingHub : IHubContext<SeatHub, ISeatClient>
    {
        public List<(string Group, IReadOnlyList<int> SeatIds)> Broadcasts { get; } = new();

        public IHubClients<ISeatClient> Clients { get; }

        public IGroupManager Groups => throw new NotSupportedException("Bộ dọn ghế không dùng Groups.");

        public RecordingHub() => Clients = new RecordingClients(this);

        private sealed class RecordingClients : IHubClients<ISeatClient>
        {
            private readonly RecordingHub _hub;
            public RecordingClients(RecordingHub hub) => _hub = hub;

            public ISeatClient Group(string groupName) => new RecordingClient(_hub, groupName);

            public ISeatClient All => Unexpected();
            public ISeatClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => Unexpected();
            public ISeatClient Client(string connectionId) => Unexpected();
            public ISeatClient Clients(IReadOnlyList<string> connectionIds) => Unexpected();
            public ISeatClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Unexpected();
            public ISeatClient Groups(IReadOnlyList<string> groupNames) => Unexpected();
            public ISeatClient User(string userId) => Unexpected();
            public ISeatClient Users(IReadOnlyList<string> userIds) => Unexpected();

            private static ISeatClient Unexpected() =>
                throw new NotSupportedException("Bộ dọn ghế chỉ được phát vào group của suất chiếu.");
        }

        private sealed class RecordingClient : ISeatClient
        {
            private readonly RecordingHub _hub;
            private readonly string _group;

            public RecordingClient(RecordingHub hub, string group)
            {
                _hub = hub;
                _group = group;
            }

            public Task SeatUnlocked(IReadOnlyList<int> seatIds)
            {
                _hub.Broadcasts.Add((_group, seatIds));
                return Task.CompletedTask;
            }

            public Task SeatSnapshot(IReadOnlyList<HeldSeat> seats) => Unexpected();
            public Task SeatHeldByMe(int seatId, int holdSeconds) => Unexpected();
            public Task SeatLocked(int seatId) => Unexpected();
            public Task SeatBooked(IReadOnlyList<int> seatIds) => Unexpected();
            public Task SeatRejected(int seatId, string reason) => Unexpected();

            private static Task Unexpected() =>
                throw new NotSupportedException("Bộ dọn ghế chỉ phát SeatUnlocked.");
        }
    }
}
