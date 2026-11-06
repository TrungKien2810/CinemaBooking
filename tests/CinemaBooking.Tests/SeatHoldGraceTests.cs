using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Infrastructure.Services;

namespace CinemaBooking.Tests;

public class SeatHoldGraceTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    [Fact]
    public void Signed_in_user_keeps_hold_after_disconnect_and_others_still_see_it_held()
    {
        var sut = new InMemorySeatLockManager();
        var key = new SeatKey(1, 10);
        sut.TryLock(key, "conn-A", "user-A", Ttl);

        Assert.Empty(sut.DetachFor("conn-A"));

        Assert.Single(sut.GetLocksForShowtime(1));
        Assert.Equal(LockResult.HeldByOther, sut.TryLock(key, "conn-B", "user-B", Ttl));
    }

    [Fact]
    public void Guest_hold_is_released_immediately_on_disconnect()
    {
        var sut = new InMemorySeatLockManager();
        var key = new SeatKey(1, 10);
        sut.TryLock(key, "conn-A", null, Ttl);

        Assert.Equal([key], sut.DetachFor("conn-A"));
        Assert.Empty(sut.GetLocksForShowtime(1));
    }

    [Fact]
    public void Same_user_gets_the_hold_back_on_a_new_connection_but_other_users_do_not()
    {
        var sut = new InMemorySeatLockManager();
        var key = new SeatKey(1, 10);
        sut.TryLock(key, "conn-A", "user-A", Ttl);
        sut.DetachFor("conn-A");

        Assert.Empty(sut.Adopt(1, "user-B", "conn-B"));
        Assert.Empty(sut.Adopt(2, "user-A", "conn-A2"));
        var adopted = sut.Adopt(1, "user-A", "conn-A2");

        Assert.Single(adopted);
        Assert.True(sut.ValidateLocks([key], "conn-A2"));
    }

    [Fact]
    public void Expired_away_hold_is_not_adopted()
    {
        var sut = new InMemorySeatLockManager();
        sut.TryLock(new SeatKey(1, 10), "conn-A", "user-A", TimeSpan.FromMilliseconds(1));
        sut.DetachFor("conn-A");
        Thread.Sleep(20);

        Assert.Empty(sut.Adopt(1, "user-A", "conn-A2"));
    }
}
