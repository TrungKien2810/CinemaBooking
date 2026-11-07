namespace CinemaBooking.Domain.Abstractions;

/// <summary>Khóa ghế luôn theo cặp (suất chiếu, ghế) — cùng một ghế vật lý ở 2 suất là 2 lock khác nhau.</summary>
public readonly record struct SeatKey(int ShowtimeId, int SeatId);

public sealed record SeatLock(SeatKey Key, string ConnectionId, string? UserRef, DateTime ExpiresUtc);

public enum LockResult { Acquired, AlreadyHeldBySelf, HeldByOther, AlreadyBooked }

/// <summary>
/// Singleton. Nguồn sự thật cho trạng thái "đang giữ" (KHÔNG ghi xuống DB).
/// Mọi thao tác phải atomic — dùng ConcurrentDictionary, không cần lock thủ công.
/// </summary>
public interface ISeatLockManager
{
    LockResult TryLock(SeatKey key, string connectionId, string? userRef, TimeSpan ttl);
    bool Release(SeatKey key, string connectionId);
    /// <summary>Giải phóng mọi ghế của một connection.</summary>
    IReadOnlyCollection<SeatKey> ReleaseAllFor(string connectionId);
    /// <summary>
    /// Connection đóng (đổi trang, đăng xuất, rớt mạng): ghế của người ĐÃ ĐĂNG NHẬP vẫn được giữ tới hết hạn (khách quay lại
    /// trong thời gian giữ ghế sẽ nhận lại bằng <see cref="Adopt"/>); ghế của khách vãng lai được nhả ngay. Trả về các ghế đã nhả.
    /// </summary>
    IReadOnlyCollection<SeatKey> DetachFor(string connectionId);
    /// <summary>Gắn lại các ghế còn hạn mà <paramref name="userRef"/> đang giữ ở suất này cho connection mới, trả về chúng.</summary>
    IReadOnlyCollection<SeatLock> Adopt(int showtimeId, string userRef, string connectionId);
    /// <summary>
    /// Chuyển quyền giữ ghế từ owner này sang owner khác và gia hạn TTL. Dùng khi khách rời
    /// trang chọn ghế sang thanh toán: connection SignalR đóng nhưng hold-token trong Session
    /// vẫn giữ được ghế cho bước đặt vé.
    /// </summary>
    bool TransferOwnership(IReadOnlyCollection<SeatKey> keys, string fromOwnerId, string toOwnerId, TimeSpan ttl);
    /// <summary>Xác nhận connection này thực sự đang giữ đủ các ghế — gọi lại ngay trước khi ghi Booking.</summary>
    bool ValidateLocks(IEnumerable<SeatKey> keys, string connectionId);
    /// <summary>Xóa lock đã hết hạn; trả về danh sách để phát SignalR báo ghế trống.</summary>
    IReadOnlyCollection<SeatKey> RemoveExpiredLocks(DateTime nowUtc);
    IReadOnlyCollection<SeatLock> GetLocksForShowtime(int showtimeId);
}
