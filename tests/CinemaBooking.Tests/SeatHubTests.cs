using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Web.Hubs;
using CinemaBooking.Web.Services;

namespace CinemaBooking.Tests;

public class SeatHubTests
{
    private const int Showtime = 7;
    private const int OtherShowtime = 8;

    private static async Task<SeatHubHarness> JoinedAsync(params (string Conn, int Showtime)[] joins)
    {
        var h = new SeatHubHarness();
        foreach (var (conn, showtime) in joins) await h.Hub(conn).JoinShowtime(showtime);
        h.ClearInboxes();
        return h;
    }

    [Fact]
    public async Task Select_locks_seat_answers_caller_and_notifies_only_others_in_the_same_showtime()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime), ("C", OtherShowtime));

        await h.Hub("A").SelectSeat(Showtime, 10);

        Assert.Equal(new SentMessage("SeatHeldByMe", new object?[] { 10, 300 }), Assert.Single(h.Inbox("A")), MessageComparer.Instance);
        Assert.Equal(new SentMessage("SeatLocked", new object?[] { 10 }), Assert.Single(h.Inbox("B")), MessageComparer.Instance);
        Assert.Empty(h.Inbox("C"));
        Assert.False(h.Locks.ValidateLocks(new[] { new SeatKey(Showtime, 10) }, "B"));
        Assert.True(h.Locks.ValidateLocks(new[] { new SeatKey(Showtime, 10) }, "A"));
    }

    [Fact]
    public async Task Select_of_a_seat_held_by_someone_else_is_rejected_without_broadcast()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        h.ClearInboxes();

        await h.Hub("B").SelectSeat(Showtime, 10);

        Assert.Equal("SeatRejected", Assert.Single(h.Inbox("B")).Method);
        Assert.Empty(h.Inbox("A"));
    }

    [Fact]
    public async Task Selecting_an_own_seat_again_renews_it_without_telling_others()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        h.ClearInboxes();

        await h.Hub("A").SelectSeat(Showtime, 10);

        Assert.Equal("SeatHeldByMe", Assert.Single(h.Inbox("A")).Method);
        Assert.Empty(h.Inbox("B"));
    }

    [Fact]
    public async Task Select_without_joining_the_showtime_takes_no_lock()
    {
        var h = new SeatHubHarness();

        await h.Hub("A").SelectSeat(Showtime, 10);

        Assert.Equal("SeatRejected", Assert.Single(h.Inbox("A")).Method);
        Assert.Empty(h.Locks.GetLocksForShowtime(Showtime));
    }

    [Theory]
    [InlineData(SeatAvailability.Booked)]
    [InlineData(SeatAvailability.NotBookable)]
    public async Task Select_of_a_seat_that_cannot_be_sold_is_rejected_without_a_lock(SeatAvailability availability)
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        h.Availability.Set(Showtime, 10, availability);

        await h.Hub("A").SelectSeat(Showtime, 10);

        Assert.Equal("SeatRejected", Assert.Single(h.Inbox("A")).Method);
        Assert.Empty(h.Inbox("B"));
        Assert.Empty(h.Locks.GetLocksForShowtime(Showtime));
    }

    [Fact]
    public async Task Select_beyond_the_per_connection_limit_is_rejected()
    {
        var h = await JoinedAsync(("A", Showtime));
        for (var seat = 1; seat <= SeatHub.MaxSeatsPerConnection; seat++)
            await h.Hub("A").SelectSeat(Showtime, seat);
        h.ClearInboxes();

        await h.Hub("A").SelectSeat(Showtime, 99);
        // Gia hạn ghế đang giữ vẫn được, dù đã chạm hạn mức.
        await h.Hub("A").SelectSeat(Showtime, 1);

        Assert.Equal(new[] { "SeatRejected", "SeatHeldByMe" }, h.Inbox("A").Select(m => m.Method));
        Assert.Equal(SeatHub.MaxSeatsPerConnection, h.Locks.GetLocksForShowtime(Showtime).Count);
    }

    [Fact]
    public async Task Join_sends_a_snapshot_with_own_seats_marked_and_their_time_left()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        await h.Hub("B").SelectSeat(Showtime, 11);
        h.ClearInboxes();

        await h.Hub("A").JoinShowtime(Showtime);

        var snapshot = (IReadOnlyList<HeldSeat>)Assert.Single(h.Inbox("A")).Args[0]!;
        Assert.Equal(2, snapshot.Count);
        var mine = snapshot[0];
        Assert.Equal((10, true), (mine.SeatId, mine.Mine));
        Assert.InRange(mine.SecondsLeft!.Value, 295, 300);
        Assert.Equal(new HeldSeat(11, false), snapshot[1]);
    }

    [Fact]
    public async Task Release_frees_the_seat_for_the_whole_group()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        h.ClearInboxes();

        await h.Hub("A").ReleaseSeat(Showtime, 10);

        foreach (var conn in new[] { "A", "B" })
        {
            var msg = Assert.Single(h.Inbox(conn));
            Assert.Equal("SeatUnlocked", msg.Method);
            Assert.Equal(new[] { 10 }, (IReadOnlyList<int>)msg.Args[0]!);
        }
        Assert.Empty(h.Locks.GetLocksForShowtime(Showtime));
    }

    [Fact]
    public async Task Release_of_someone_elses_seat_only_resyncs_the_caller()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        h.ClearInboxes();

        await h.Hub("B").ReleaseSeat(Showtime, 10);

        Assert.Equal(new SentMessage("SeatLocked", new object?[] { 10 }), Assert.Single(h.Inbox("B")), MessageComparer.Instance);
        Assert.Empty(h.Inbox("A"));
        Assert.True(h.Locks.ValidateLocks(new[] { new SeatKey(Showtime, 10) }, "A"));
    }

    [Fact]
    public async Task Disconnect_releases_every_seat_with_one_message_per_showtime()
    {
        var h = await JoinedAsync(("A", Showtime), ("A", OtherShowtime), ("B", Showtime), ("C", OtherShowtime));
        await h.Hub("A").SelectSeat(Showtime, 12);
        await h.Hub("A").SelectSeat(Showtime, 10);
        await h.Hub("A").SelectSeat(OtherShowtime, 10);
        h.ClearInboxes();

        await h.DisconnectAsync("A");

        var toB = Assert.Single(h.Inbox("B"));
        Assert.Equal("SeatUnlocked", toB.Method);
        Assert.Equal(new[] { 10, 12 }, (IReadOnlyList<int>)toB.Args[0]!);
        Assert.Equal(new[] { 10 }, (IReadOnlyList<int>)Assert.Single(h.Inbox("C")).Args[0]!);
        Assert.Empty(h.Locks.GetLocksForShowtime(Showtime));
        Assert.Empty(h.Locks.GetLocksForShowtime(OtherShowtime));
    }

    private sealed class MessageComparer : IEqualityComparer<SentMessage>
    {
        public static readonly MessageComparer Instance = new();

        public bool Equals(SentMessage? x, SentMessage? y) =>
            x is not null && y is not null && x.Method == y.Method && x.Args.SequenceEqual(y.Args);

        public int GetHashCode(SentMessage obj) => obj.Method.GetHashCode();
    }
}
