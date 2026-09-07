using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Infrastructure.Services;

namespace CinemaBooking.Tests;

public class SeatLockManagerTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    [Fact]
    public void TryLock_first_caller_wins()
    {
        var sut = new InMemorySeatLockManager();
        var key = new SeatKey(1, 10);

        Assert.Equal(LockResult.Acquired, sut.TryLock(key, "conn-A", null, Ttl));
        Assert.Equal(LockResult.HeldByOther, sut.TryLock(key, "conn-B", null, Ttl));
    }

    [Fact]
    public void Concurrent_TryLock_grants_exactly_one()
    {
        var sut = new InMemorySeatLockManager();
        var key = new SeatKey(1, 10);

        var results = new LockResult[64];
        Parallel.For(0, results.Length, i =>
            results[i] = sut.TryLock(key, $"conn-{i}", null, Ttl));

        Assert.Equal(1, results.Count(r => r == LockResult.Acquired));
    }

    [Fact]
    public void Release_by_other_connection_is_ignored()
    {
        var sut = new InMemorySeatLockManager();
        var key = new SeatKey(1, 10);
        sut.TryLock(key, "conn-A", null, Ttl);

        Assert.False(sut.Release(key, "conn-B"));
        Assert.True(sut.ValidateLocks(new[] { key }, "conn-A"));
    }

    [Fact]
    public void Expired_lock_is_removed_and_reported()
    {
        var sut = new InMemorySeatLockManager();
        var key = new SeatKey(1, 10);
        sut.TryLock(key, "conn-A", null, TimeSpan.FromMilliseconds(-1));

        var expired = sut.RemoveExpiredLocks(DateTime.UtcNow);

        Assert.Contains(key, expired);
        Assert.False(sut.ValidateLocks(new[] { key }, "conn-A"));
    }

    [Fact]
    public void Same_seat_different_showtime_are_independent_locks()
    {
        var sut = new InMemorySeatLockManager();

        Assert.Equal(LockResult.Acquired, sut.TryLock(new SeatKey(1, 10), "conn-A", null, Ttl));
        Assert.Equal(LockResult.Acquired, sut.TryLock(new SeatKey(2, 10), "conn-B", null, Ttl));
    }
}
