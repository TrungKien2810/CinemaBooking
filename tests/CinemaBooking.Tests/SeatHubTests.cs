using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Web.Hubs;
using CinemaBooking.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Tests;

/// <summary>Giữ ghế khi chọn: hạn cố định 10 phút theo (khách, suất), lưu DB, không gia hạn được.</summary>
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
    public async Task Select_holds_the_seat_for_the_caller_with_the_full_deadline_and_tells_others_in_the_showtime()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime), ("C", OtherShowtime));

        await h.Hub("A").SelectSeat(Showtime, 10);

        Assert.Equal(new SentMessage("SeatHeldByMe", new object?[] { 10, 600 }), Assert.Single(h.Inbox("A")), MessageComparer.Instance);
        Assert.Equal(new SentMessage("SeatLocked", new object?[] { 10 }), Assert.Single(h.Inbox("B")), MessageComparer.Instance);
        Assert.Empty(h.Inbox("C"));
    }

    [Fact]
    public async Task A_second_person_is_blocked_while_the_seat_is_held()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        h.ClearInboxes();

        await h.Hub("B").SelectSeat(Showtime, 10);

        Assert.Equal("SeatRejected", Assert.Single(h.Inbox("B")).Method);
        Assert.Empty(h.Inbox("A"));
    }

    [Fact]
    public async Task Reselecting_a_seat_does_not_move_the_deadline_and_tells_nobody()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        h.Clock.Advance(TimeSpan.FromMinutes(4));
        h.ClearInboxes();

        await h.Hub("A").SelectSeat(Showtime, 10);

        Assert.Equal(new SentMessage("SeatHeldByMe", new object?[] { 10, 360 }), Assert.Single(h.Inbox("A")), MessageComparer.Instance);
        Assert.Empty(h.Inbox("B"));
    }

    [Fact]
    public async Task Deselecting_everything_and_selecting_again_does_not_restart_the_clock()
    {
        var h = await JoinedAsync(("A", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        h.Clock.Advance(TimeSpan.FromMinutes(6));
        await h.Hub("A").ReleaseSeat(Showtime, 10);
        h.ClearInboxes();

        await h.Hub("A").SelectSeat(Showtime, 11);

        Assert.Equal(new SentMessage("SeatHeldByMe", new object?[] { 11, 240 }), Assert.Single(h.Inbox("A")), MessageComparer.Instance);
    }

    [Fact]
    public async Task Once_the_hold_expires_another_person_can_take_the_seat_and_the_old_holder_is_refused_at_booking()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        h.Clock.Advance(TimeSpan.FromMinutes(10));
        h.ClearInboxes();

        await h.Hub("B").SelectSeat(Showtime, 10);

        Assert.Equal("SeatHeldByMe", Assert.Single(h.Inbox("B")).Method);
        Assert.False(await h.Holds().ClaimAsync("A", Showtime, new[] { 10 }));
        Assert.True(await h.Holds().ClaimAsync("B", Showtime, new[] { 10 }));
    }

    [Fact]
    public async Task Claiming_needs_every_seat_held_by_the_caller_and_still_unexpired()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        await h.Hub("B").SelectSeat(Showtime, 11);

        Assert.False(await h.Holds().ClaimAsync("A", Showtime, new[] { 10, 11 })); // 11 là của người khác
        Assert.False(await h.Holds().ClaimAsync("A", OtherShowtime, new[] { 10 })); // sai suất

        h.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.False(await h.Holds().ClaimAsync("A", Showtime, new[] { 10 }));      // hết hạn
    }

    [Fact]
    public async Task The_sweep_frees_expired_holds_and_leaves_live_ones()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await h.Hub("B").SelectSeat(Showtime, 11);
        h.Clock.Advance(TimeSpan.FromMinutes(5)); // A hết hạn, B còn 5 phút

        var freed = await h.Holds().SweepExpiredAsync();

        var released = Assert.Single(freed);
        Assert.Equal(Showtime, released.ShowtimeId);
        Assert.Equal(new[] { 10 }, released.SeatIds);
        await using var db = h.NewDb();
        Assert.Equal(new[] { 11 }, await db.SeatHolds.Select(x => x.SeatId).ToArrayAsync());
    }

    [Fact]
    public async Task Release_frees_the_seat_for_others_and_tells_them()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        h.ClearInboxes();

        await h.Hub("A").ReleaseSeat(Showtime, 10);
        await h.Hub("B").SelectSeat(Showtime, 10);

        Assert.Equal(new[] { "SeatUnlocked", "SeatHeldByMe" }, h.Inbox("B").Select(m => m.Method));
    }

    [Fact]
    public async Task Nobody_can_release_someone_elses_seat()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        h.ClearInboxes();

        await h.Hub("B").ReleaseSeat(Showtime, 10);

        Assert.Empty(h.Inbox("A"));
        await using var db = h.NewDb();
        Assert.Equal("A", (await db.SeatHolds.SingleAsync()).UserId);
    }

    [Fact]
    public async Task Join_sends_a_snapshot_with_my_seats_and_deadline_and_other_peoples_seats_without_theirs()
    {
        var h = await JoinedAsync(("A", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        await h.Hub("B").JoinShowtime(Showtime);
        await h.Hub("B").SelectSeat(Showtime, 11);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.ClearInboxes();

        await h.Hub("A").JoinShowtime(Showtime);

        var seats = (IReadOnlyList<HeldSeat>)Assert.Single(h.Inbox("A")).Args[0]!;
        Assert.Equal(new HeldSeat(10, true, 540), seats.Single(s => s.SeatId == 10));
        Assert.Equal(new HeldSeat(11, false, null), seats.Single(s => s.SeatId == 11));
    }

    [Fact]
    public async Task Losing_the_connection_keeps_the_hold_until_the_deadline()
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        await h.Hub("A").SelectSeat(Showtime, 10);
        h.ClearInboxes();

        await h.DisconnectAsync("A");
        await h.Hub("B").SelectSeat(Showtime, 10);

        Assert.Equal("SeatRejected", Assert.Single(h.Inbox("B")).Method);
    }

    [Fact]
    public async Task Select_without_joining_the_showtime_is_rejected()
    {
        var h = new SeatHubHarness();

        await h.Hub("A").SelectSeat(Showtime, 10);

        Assert.Equal("SeatRejected", Assert.Single(h.Inbox("A")).Method);
    }

    [Theory]
    [InlineData(SeatAvailability.Booked)]
    [InlineData(SeatAvailability.NotBookable)]
    public async Task Select_of_a_seat_that_cannot_be_sold_is_rejected_for_the_caller_only_and_takes_no_hold(SeatAvailability availability)
    {
        var h = await JoinedAsync(("A", Showtime), ("B", Showtime));
        h.Availability.Set(Showtime, 10, availability);

        await h.Hub("A").SelectSeat(Showtime, 10);

        Assert.Equal("SeatRejected", Assert.Single(h.Inbox("A")).Method);
        Assert.Empty(h.Inbox("B"));
        await using var db = h.NewDb();
        Assert.Empty(db.SeatHolds);
    }

    [Fact]
    public async Task Select_beyond_the_per_person_limit_is_rejected_but_reselecting_is_not()
    {
        var h = await JoinedAsync(("A", Showtime));
        for (var seat = 1; seat <= SeatHub.MaxSeatsPerConnection; seat++)
            await h.Hub("A").SelectSeat(Showtime, seat);
        h.ClearInboxes();

        await h.Hub("A").SelectSeat(Showtime, 99);
        await h.Hub("A").SelectSeat(Showtime, 1);

        Assert.Equal(new[] { "SeatRejected", "SeatHeldByMe" }, h.Inbox("A").Select(m => m.Method));
    }

    private sealed class MessageComparer : IEqualityComparer<SentMessage>
    {
        public static readonly MessageComparer Instance = new();

        public bool Equals(SentMessage? x, SentMessage? y) =>
            x is not null && y is not null && x.Method == y.Method && x.Args.SequenceEqual(y.Args);

        public int GetHashCode(SentMessage obj) => obj.Method.GetHashCode();
    }
}
