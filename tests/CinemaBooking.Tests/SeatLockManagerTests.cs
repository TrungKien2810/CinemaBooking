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

    // TTL âm = lock đã hết hạn ngay khi tạo, mô phỏng người giữ bỏ đi mà ExpiredSeatCleaner chưa
    // kịp quét (chu kỳ 10 giây). Người sau không phải đợi cleaner.
    private static readonly TimeSpan AlreadyExpired = TimeSpan.FromMilliseconds(-1);

    [Fact]
    public void Expired_lock_can_be_stolen_before_the_cleaner_runs()
    {
        var sut = new InMemorySeatLockManager();
        var key = new SeatKey(1, 10);
        sut.TryLock(key, "conn-A", null, AlreadyExpired);

        Assert.Equal(LockResult.Acquired, sut.TryLock(key, "conn-B", null, Ttl));

        var holder = Assert.Single(sut.GetLocksForShowtime(1));
        Assert.Equal("conn-B", holder.ConnectionId);
    }

    [Fact]
    public void Previous_holder_loses_a_stolen_lock_for_good()
    {
        var sut = new InMemorySeatLockManager();
        var key = new SeatKey(1, 10);
        sut.TryLock(key, "conn-A", null, AlreadyExpired);
        sut.TryLock(key, "conn-B", null, Ttl);

        // A không đặt vé được, không nhả hộ được lock của B, và chọn lại thì bị từ chối.
        Assert.False(sut.ValidateLocks(new[] { key }, "conn-A"));
        Assert.False(sut.Release(key, "conn-A"));
        Assert.Empty(sut.ReleaseAllFor("conn-A"));
        Assert.Equal(LockResult.HeldByOther, sut.TryLock(key, "conn-A", null, Ttl));
        Assert.True(sut.ValidateLocks(new[] { key }, "conn-B"));
    }

    [Fact]
    public void Cleaner_does_not_report_a_lock_that_was_stolen_and_is_live_again()
    {
        var sut = new InMemorySeatLockManager();
        var key = new SeatKey(1, 10);
        sut.TryLock(key, "conn-A", null, AlreadyExpired);
        sut.TryLock(key, "conn-B", null, Ttl);

        // Nếu cleaner báo ghế này "trống", mọi client sẽ vẽ trống một ghế B đang giữ.
        Assert.Empty(sut.RemoveExpiredLocks(DateTime.UtcNow));
        Assert.Single(sut.GetLocksForShowtime(1));
    }

    [Fact]
    public void Concurrent_steal_of_an_expired_lock_grants_exactly_one()
    {
        var sut = new InMemorySeatLockManager();
        var key = new SeatKey(1, 10);
        sut.TryLock(key, "conn-old", null, AlreadyExpired);

        var results = new LockResult[64];
        Parallel.For(0, results.Length, i =>
            results[i] = sut.TryLock(key, $"conn-{i}", null, Ttl));

        Assert.Equal(1, results.Count(r => r == LockResult.Acquired));
        Assert.Equal(results.Length - 1, results.Count(r => r == LockResult.HeldByOther));
    }
}
