using System.Security.Claims;
using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Services;
using CinemaBooking.Web.Hubs;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

/// <summary>
/// Dựng SeatHub như SignalR dựng: mỗi invocation một instance hub mới, còn lock manager (Singleton),
/// group membership và Context.Items (theo connection) thì sống xuyên suốt. Tin nhắn được "phát"
/// tới từng connection theo đúng ngữ nghĩa Caller / Group / OthersInGroup để test đọc được ai nhận gì.
/// </summary>
internal sealed class SeatHubHarness
{
    private readonly Dictionary<string, HashSet<string>> _groups = new();
    private readonly Dictionary<string, Dictionary<object, object?>> _items = new();
    private readonly Dictionary<string, List<SentMessage>> _inbox = new();

    public InMemorySeatLockManager Locks { get; } = new();
    public FakeSeatAvailability Availability { get; } = new();
    public SeatHoldOptions Hold { get; } = new() { DurationMinutes = 5 };

    /// <summary>Tạo hub cho một lần gọi từ <paramref name="connectionId"/>.</summary>
    public SeatHub Hub(string connectionId)
    {
        if (!_items.ContainsKey(connectionId)) _items[connectionId] = new Dictionary<object, object?>();
        Inbox(connectionId);

        return new SeatHub(Locks, Options.Create(Hold), Availability)
        {
            Context = new FakeCallerContext(connectionId, _items[connectionId]),
            Clients = new FakeClients(this, connectionId),
            Groups = new FakeGroups(this),
        };
    }

    public async Task DisconnectAsync(string connectionId)
    {
        await Hub(connectionId).OnDisconnectedAsync(null);
        foreach (var members in _groups.Values) members.Remove(connectionId);
    }

    public List<SentMessage> Inbox(string connectionId) =>
        _inbox.TryGetValue(connectionId, out var list) ? list : _inbox[connectionId] = new List<SentMessage>();

    public void ClearInboxes()
    {
        foreach (var list in _inbox.Values) list.Clear();
    }

    internal IEnumerable<string> Members(string group) =>
        _groups.TryGetValue(group, out var members) ? members.ToList() : Enumerable.Empty<string>();

    internal void AddToGroup(string connectionId, string group)
    {
        if (!_groups.TryGetValue(group, out var members)) _groups[group] = members = new HashSet<string>();
        members.Add(connectionId);
    }

    internal void RemoveFromGroup(string connectionId, string group)
    {
        if (_groups.TryGetValue(group, out var members)) members.Remove(connectionId);
    }

    internal ISeatClient To(IEnumerable<string> connectionIds) =>
        new RecordingClient(this, connectionIds.ToList());

    internal void Record(string connectionId, string method, params object?[] args) =>
        Inbox(connectionId).Add(new SentMessage(method, args));

    private sealed class RecordingClient : ISeatClient
    {
        private readonly SeatHubHarness _h;
        private readonly IReadOnlyList<string> _targets;

        public RecordingClient(SeatHubHarness h, IReadOnlyList<string> targets)
        {
            _h = h;
            _targets = targets;
        }

        private Task Send(string method, params object?[] args)
        {
            foreach (var target in _targets) _h.Record(target, method, args);
            return Task.CompletedTask;
        }

        public Task SeatSnapshot(IReadOnlyList<HeldSeat> seats) => Send(nameof(SeatSnapshot), seats);
        public Task SeatHeldByMe(int seatId, int holdSeconds) => Send(nameof(SeatHeldByMe), seatId, holdSeconds);
        public Task SeatLocked(int seatId) => Send(nameof(SeatLocked), seatId);
        public Task SeatUnlocked(IReadOnlyList<int> seatIds) => Send(nameof(SeatUnlocked), seatIds);
        public Task SeatBooked(IReadOnlyList<int> seatIds) => Send(nameof(SeatBooked), seatIds);
        public Task SeatRejected(int seatId, string reason) => Send(nameof(SeatRejected), seatId, reason);
    }

    private sealed class FakeClients : IHubCallerClients<ISeatClient>
    {
        private readonly SeatHubHarness _h;
        private readonly string _caller;

        public FakeClients(SeatHubHarness h, string caller)
        {
            _h = h;
            _caller = caller;
        }

        public ISeatClient Caller => _h.To(new[] { _caller });
        public ISeatClient Group(string groupName) => _h.To(_h.Members(groupName));
        public ISeatClient OthersInGroup(string groupName) => _h.To(_h.Members(groupName).Where(c => c != _caller));

        // Hub không dùng tới các đường phát rộng hơn — gọi nhầm là lỗi logic, cho test nổ luôn.
        public ISeatClient Others => throw Unexpected();
        public ISeatClient All => throw Unexpected();
        public ISeatClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw Unexpected();
        public ISeatClient Client(string connectionId) => throw Unexpected();
        public ISeatClient Clients(IReadOnlyList<string> connectionIds) => throw Unexpected();
        public ISeatClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw Unexpected();
        public ISeatClient Groups(IReadOnlyList<string> groupNames) => throw Unexpected();
        public ISeatClient User(string userId) => throw Unexpected();
        public ISeatClient Users(IReadOnlyList<string> userIds) => throw Unexpected();

        private static NotSupportedException Unexpected() => new("SeatHub không được phát ra ngoài group của suất chiếu.");
    }

    private sealed class FakeGroups : IGroupManager
    {
        private readonly SeatHubHarness _h;

        public FakeGroups(SeatHubHarness h) => _h = h;

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            _h.AddToGroup(connectionId, groupName);
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            _h.RemoveFromGroup(connectionId, groupName);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCallerContext : HubCallerContext
    {
        public FakeCallerContext(string connectionId, IDictionary<object, object?> items)
        {
            ConnectionId = connectionId;
            Items = items;
        }

        public override string ConnectionId { get; }
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => new(new ClaimsIdentity());
        public override IDictionary<object, object?> Items { get; }
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}

internal sealed record SentMessage(string Method, object?[] Args);

/// <summary>Mặc định mọi ghế còn trống; test đánh dấu từng ghế đã đặt / không bán.</summary>
internal sealed class FakeSeatAvailability : ISeatAvailabilityQuery
{
    private readonly Dictionary<SeatKey, SeatAvailability> _overrides = new();

    public void Set(int showtimeId, int seatId, SeatAvailability value) =>
        _overrides[new SeatKey(showtimeId, seatId)] = value;

    public Task<SeatAvailability> CheckAsync(int showtimeId, int seatId, CancellationToken ct = default) =>
        Task.FromResult(_overrides.GetValueOrDefault(new SeatKey(showtimeId, seatId), SeatAvailability.Available));
}
