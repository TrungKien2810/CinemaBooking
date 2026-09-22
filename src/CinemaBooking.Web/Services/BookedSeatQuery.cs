using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

public sealed class BookedSeatQuery : IBookedSeatQuery
{
    private readonly CinemaDbContext _db;

    public BookedSeatQuery(CinemaDbContext db) => _db = db;

    public async Task<IReadOnlySet<int>> GetBookedSeatIdsAsync(int showtimeId, CancellationToken ct = default)
    {
        // Đi thẳng vào BookingSeats.ShowtimeId (cột có unique index) — không cần join Bookings.
        var seatIds = await _db.BookingSeats.AsNoTracking()
            .Where(bs => bs.ShowtimeId == showtimeId)
            .Select(bs => bs.SeatId)
            .ToListAsync(ct);

        return seatIds.ToHashSet();
    }
}
