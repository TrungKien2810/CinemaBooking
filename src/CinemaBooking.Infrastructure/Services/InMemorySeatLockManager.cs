using System.Collections.Concurrent;
using CinemaBooking.Domain.Abstractions;

namespace CinemaBooking.Infrastructure.Services;

/// <summary>
/// Lưu lock trong ConcurrentDictionary. TryAdd/TryRemove đã atomic sẵn nên
/// KHÔNG cần SemaphoreSlim cho từng ghế.
/// Giới hạn: chỉ đúng khi chạy 1 process. Nhiều instance -> đổi sang Redis (xem IDEAS.md).
/// </summary>
public sealed class InMemorySeatLockManager : ISeatLockManager
{
    private readonly ConcurrentDictionary<SeatKey, SeatLock> _locks = new();

    public LockResult TryLock(SeatKey key, string connectionId, string? userRef, TimeSpan ttl)
    {
        var now = DateTime.UtcNow;
        var candidate = new SeatLock(key, connectionId, userRef, now.Add(ttl));

        while (true)
        {
            if (_locks.TryAdd(key, candidate)) return LockResult.Acquired;
            if (!_locks.TryGetValue(key, out var existing)) continue;

            if (existing.ConnectionId == connectionId)
            {
                // Gia hạn lock của chính mình.
                return _locks.TryUpdate(key, candidate, existing)
                    ? LockResult.AlreadyHeldBySelf
                    : LockResult.AlreadyHeldBySelf;
            }

            // Lock của người khác đã hết hạn nhưng cleaner chưa kịp chạy -> cướp lock.
            if (existing.ExpiresUtc <= now && _locks.TryUpdate(key, candidate, existing))
                return LockResult.Acquired;

            if (existing.ExpiresUtc > now) return LockResult.HeldByOther;
        }
    }

    public bool Release(SeatKey key, string connectionId)
    {
        if (!_locks.TryGetValue(key, out var existing) || existing.ConnectionId != connectionId)
            return false;
        return ((ICollection<KeyValuePair<SeatKey, SeatLock>>)_locks)
            .Remove(new KeyValuePair<SeatKey, SeatLock>(key, existing));
    }

    public IReadOnlyCollection<SeatKey> ReleaseAllFor(string connectionId)
    {
        var released = new List<SeatKey>();
        foreach (var kv in _locks)
        {
            if (kv.Value.ConnectionId == connectionId && Release(kv.Key, connectionId))
                released.Add(kv.Key);
        }
        return released;
    }

    public bool ValidateLocks(IEnumerable<SeatKey> keys, string connectionId)
    {
        var now = DateTime.UtcNow;
        return keys.All(k => _locks.TryGetValue(k, out var l)
                             && l.ConnectionId == connectionId
                             && l.ExpiresUtc > now);
    }

    public IReadOnlyCollection<SeatKey> RemoveExpiredLocks(DateTime nowUtc)
    {
        var expired = new List<SeatKey>();
        foreach (var kv in _locks)
        {
            if (kv.Value.ExpiresUtc > nowUtc) continue;
            if (((ICollection<KeyValuePair<SeatKey, SeatLock>>)_locks).Remove(kv))
                expired.Add(kv.Key);
        }
        return expired;
    }

    public IReadOnlyCollection<SeatLock> GetLocksForShowtime(int showtimeId)
    {
        var now = DateTime.UtcNow;
        return _locks.Values
            .Where(l => l.Key.ShowtimeId == showtimeId && l.ExpiresUtc > now)
            .ToList();
    }
}
