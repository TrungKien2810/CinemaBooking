using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Hết hạn đơn <c>Pending</c> bằng UPDATE có điều kiện (<c>WHERE Status = Pending</c>) thay vì đọc rồi sửa:
/// IPN xác nhận đơn đúng lúc đang quét sẽ thắng, đơn <c>Confirmed</c> không bị ghi đè thành Expired
/// và ghế đã bán không bị trả lại.
/// </summary>
internal static class PendingExpiry
{
    /// <summary>Trả về các ghế thật sự được trả (nhóm theo suất chiếu) — chỉ từ những đơn mà lần UPDATE này giành được.</summary>
    public static async Task<List<(int ShowtimeId, List<int> SeatIds)>> ExpireAsync(
        CinemaDbContext db, DateTime cutoff, int? showtimeId, int take, CancellationToken ct)
    {
        var candidates = await db.Bookings
            .Where(b => b.Status == BookingStatus.Pending && b.BookingTimeUtc <= cutoff
                     && (showtimeId == null || b.ShowtimeId == showtimeId))
            .OrderBy(b => b.BookingTimeUtc) // đơn cũ nhất trước: Take phải xác định, không phụ thuộc thứ tự DB trả về
            .Select(b => new { b.Id, b.ShowtimeId })
            .Take(take)
            .ToListAsync(ct);

        var freed = new List<(int ShowtimeId, int SeatId)>();
        var ownsTransaction = db.Database.CurrentTransaction is null;

        foreach (var c in candidates)
        {
            await using var tx = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;

            var claimed = await db.Bookings
                .Where(b => b.Id == c.Id && b.Status == BookingStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Expired), ct);
            if (claimed == 0) continue; // đơn vừa được xác nhận/hủy — không đụng vào ghế của nó

            var seatIds = await db.BookingSeats.Where(s => s.BookingId == c.Id).Select(s => s.SeatId).ToListAsync(ct);
            await db.BookingSeats.Where(s => s.BookingId == c.Id).ExecuteDeleteAsync(ct);
            await db.Payments.Where(p => p.BookingId == c.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Failed), ct);

            if (tx is not null) await tx.CommitAsync(ct);
            freed.AddRange(seatIds.Select(id => (c.ShowtimeId, id)));
        }

        return freed
            .GroupBy(x => x.ShowtimeId)
            .Select(g => (g.Key, g.Select(x => x.SeatId).OrderBy(id => id).ToList()))
            .ToList();
    }
}
