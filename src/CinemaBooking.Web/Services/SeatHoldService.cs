using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Services;

/// <summary>Kết quả chọn ghế: <see cref="SecondsLeft"/> là số giây còn lại của hạn giữ (chung cho mọi ghế của khách).</summary>
public sealed record SeatHoldResult(bool Ok, string? Reason, int SecondsLeft = 0, bool IsNew = false, IReadOnlyList<int>? Released = null)
{
    public static SeatHoldResult Held(int secondsLeft, bool isNew) => new(true, null, secondsLeft, isNew);
    public static SeatHoldResult Rejected(string reason) => new(false, reason);
}

/// <summary>
/// Giữ ghế khi chọn, lưu trong DB (không dùng RAM nên sống qua restart và chạy được nhiều instance).
/// <para>
/// Hạn giữ cố định, KHÔNG gia hạn: nằm ở <see cref="SeatHoldSession"/> theo (UserId, ShowtimeId), nên chọn
/// lại hay bỏ chọn hết rồi chọn lại cũng không có thêm thời gian. Chốt chặn là hai unique index
/// ((UserId, ShowtimeId) cho session, (ShowtimeId, SeatId) cho ghế) — mọi đường đua đều kết thúc ở một
/// vi phạm unique được dịch thành thông báo thân thiện, không cần khóa.
/// </para>
/// </summary>
public sealed class SeatHoldService
{
    private readonly CinemaDbContext _db;
    private readonly TimeProvider _clock;
    private readonly SeatHoldOptions _options;

    public SeatHoldService(CinemaDbContext db, TimeProvider clock, IOptions<SeatHoldOptions> options)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<SeatHoldResult> SelectAsync(string userId, int showtimeId, int seatId, int maxSeats, CancellationToken ct = default)
    {
        var now = Now;
        List<int>? released = null; // ghế của chính khách đã hết hạn và bị dọn ở đây — sweeper sẽ không thấy nữa nên phải báo

        var session = await _db.SeatHoldSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.ShowtimeId == showtimeId, ct);

        if (session is not null && session.ExpiresUtc <= now)
        {
            // Hạn cũ đã hết: bỏ cả session lẫn ghế của nó rồi mở hạn mới (không phải gia hạn — đã hết thật).
            released = await _db.SeatHolds.Where(h => h.UserId == userId && h.ShowtimeId == showtimeId).Select(h => h.SeatId).ToListAsync(ct);
            // Chỉ xoá hold KHÔNG còn session sống: tab thứ hai có thể vừa mở session mới và chèn hold mới.
            await _db.SeatHolds
                .Where(h => h.UserId == userId && h.ShowtimeId == showtimeId
                    && !_db.SeatHoldSessions.Any(x => x.UserId == h.UserId && x.ShowtimeId == h.ShowtimeId && x.ExpiresUtc > now))
                .ExecuteDeleteAsync(ct);
            await _db.SeatHoldSessions.Where(s => s.Id == session.Id && s.ExpiresUtc <= now).ExecuteDeleteAsync(ct);
            session = null;
        }

        if (session is null)
        {
            _db.SeatHoldSessions.Add(new SeatHoldSession
            {
                UserId = userId,
                ShowtimeId = showtimeId,
                ExpiresUtc = now + _options.Duration,
            });
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (BookingService.IsUniqueViolation(ex))
            {
                // Hai lần chọn cùng lúc của một khách (mở 2 tab): bên kia đã tạo session — dùng lại hạn đó.
            }
            _db.ChangeTracker.Clear();
            session = await _db.SeatHoldSessions.AsNoTracking()
                .FirstAsync(s => s.UserId == userId && s.ShowtimeId == showtimeId, ct);
        }

        var secondsLeft = SecondsLeft(session.ExpiresUtc, now);

        var mine = await _db.SeatHolds.AsNoTracking()
            .Where(h => h.UserId == userId && h.ShowtimeId == showtimeId)
            .Select(h => h.SeatId).ToListAsync(ct);
        if (mine.Contains(seatId)) return SeatHoldResult.Held(secondsLeft, isNew: false) with { Released = released };
        if (mine.Count >= maxSeats) return SeatHoldResult.Rejected($"Mỗi đơn chỉ đặt tối đa {maxSeats} ghế.") with { Released = released };

        // Hold hết hạn của người khác vẫn chiếm chỗ trong unique index: xóa nó trước khi chèn.
        await DeleteExpiredHoldsOfSeatAsync(showtimeId, seatId, now, ct);

        _db.SeatHolds.Add(new SeatHold { UserId = userId, ShowtimeId = showtimeId, SeatId = seatId });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (BookingService.IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return SeatHoldResult.Rejected("Ghế đang được người khác giữ.");
        }
        _db.ChangeTracker.Clear();
        return SeatHoldResult.Held(secondsLeft, isNew: true) with { Released = released };
    }

    /// <summary>Bỏ chọn: chỉ xóa ghế của chính khách (session và hạn giữ nguyên). Trả true nếu có ghế được nhả.</summary>
    public async Task<bool> ReleaseAsync(string userId, int showtimeId, int seatId, CancellationToken ct = default) =>
        await _db.SeatHolds.Where(h => h.UserId == userId && h.ShowtimeId == showtimeId && h.SeatId == seatId)
            .ExecuteDeleteAsync(ct) > 0;

    /// <summary>Các ghế đang được giữ còn hạn của suất — cho trang vừa mở: ghế của mình và ghế của người khác.</summary>
    public async Task<List<(int SeatId, bool Mine, int SecondsLeft)>> SnapshotAsync(string userId, int showtimeId, CancellationToken ct = default)
    {
        var now = Now;
        var rows = await (from h in _db.SeatHolds.AsNoTracking()
                          join s in _db.SeatHoldSessions.AsNoTracking()
                              on new { h.UserId, h.ShowtimeId } equals new { s.UserId, s.ShowtimeId }
                          where h.ShowtimeId == showtimeId && s.ExpiresUtc > now
                          select new { h.SeatId, h.UserId, s.ExpiresUtc }).ToListAsync(ct);
        return rows.Select(r => (r.SeatId, r.UserId == userId, SecondsLeft(r.ExpiresUtc, now))).ToList();
    }

    /// <summary>
    /// Nhận các ghế đã giữ để đặt đơn: DELETE có điều kiện (đúng người, còn hạn) và đòi đúng bấy nhiêu dòng.
    /// Phải gọi trong cùng transaction với việc ghi BookingSeats — nếu thiếu dòng thì rollback. Đọc rồi mới
    /// kiểm tra không đủ vì người khác có thể xóa hold hết hạn của ta ngay giữa lúc đọc và lúc ghi.
    /// </summary>
    public async Task<bool> ClaimAsync(string userId, int showtimeId, IReadOnlyCollection<int> seatIds, CancellationToken ct = default)
    {
        var now = Now;
        var claimed = await _db.SeatHolds
            .Where(h => h.UserId == userId && h.ShowtimeId == showtimeId && seatIds.Contains(h.SeatId)
                && _db.SeatHoldSessions.Any(s => s.UserId == h.UserId && s.ShowtimeId == h.ShowtimeId && s.ExpiresUtc > now))
            .ExecuteDeleteAsync(ct);
        if (claimed != seatIds.Count) return false;

        // Đơn đã nhận ghế: bỏ nốt ghế còn lại (nếu khách chọn dư) và session.
        await _db.SeatHolds.Where(h => h.UserId == userId && h.ShowtimeId == showtimeId).ExecuteDeleteAsync(ct);
        await _db.SeatHoldSessions.Where(s => s.UserId == userId && s.ShowtimeId == showtimeId).ExecuteDeleteAsync(ct);
        return true;
    }

    /// <summary>Quét session hết hạn, xóa ghế của chúng và trả về ghế được nhả (nhóm theo suất) để phát SeatUnlocked.</summary>
    public async Task<List<(int ShowtimeId, List<int> SeatIds)>> SweepExpiredAsync(CancellationToken ct = default)
    {
        var now = Now;
        var expired = await _db.SeatHoldSessions.AsNoTracking()
            .Where(s => s.ExpiresUtc <= now).Take(500).ToListAsync(ct);

        var freed = new List<(int ShowtimeId, int SeatId)>();
        foreach (var s in expired)
        {
            var seatIds = await _db.SeatHolds.Where(h => h.UserId == s.UserId && h.ShowtimeId == s.ShowtimeId)
                .Select(h => h.SeatId).ToListAsync(ct);
            // Điều kiện "không còn session sống" đặt ngay trong DELETE: nếu khách vừa chọn lại (session mới) thì không xoá nhầm hold mới.
            await _db.SeatHolds
                .Where(h => h.UserId == s.UserId && h.ShowtimeId == s.ShowtimeId
                    && !_db.SeatHoldSessions.Any(x => x.UserId == h.UserId && x.ShowtimeId == h.ShowtimeId && x.ExpiresUtc > now))
                .ExecuteDeleteAsync(ct);
            await _db.SeatHoldSessions.Where(x => x.Id == s.Id && x.ExpiresUtc <= now).ExecuteDeleteAsync(ct);
            // Chỉ báo những ghế thật sự đã hết dòng giữ (khách có thể vừa chọn lại → hold mới còn nguyên).
            var stillHeld = await _db.SeatHolds.Where(h => h.UserId == s.UserId && h.ShowtimeId == s.ShowtimeId)
                .Select(h => h.SeatId).ToListAsync(ct);
            freed.AddRange(seatIds.Where(id => !stillHeld.Contains(id)).Select(id => (s.ShowtimeId, id)));
        }

        return freed.GroupBy(x => x.ShowtimeId)
            .Select(g => (g.Key, g.Select(x => x.SeatId).OrderBy(id => id).ToList())).ToList();
    }

    private Task<int> DeleteExpiredHoldsOfSeatAsync(int showtimeId, int seatId, DateTime now, CancellationToken ct) =>
        _db.SeatHolds
            .Where(h => h.ShowtimeId == showtimeId && h.SeatId == seatId
                && !_db.SeatHoldSessions.Any(s => s.UserId == h.UserId && s.ShowtimeId == h.ShowtimeId && s.ExpiresUtc > now))
            .ExecuteDeleteAsync(ct);

    private static int SecondsLeft(DateTime expiresUtc, DateTime now) => Math.Max(0, (int)Math.Ceiling((expiresUtc - now).TotalSeconds));
}
