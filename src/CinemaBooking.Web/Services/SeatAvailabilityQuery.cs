using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

public sealed class SeatAvailabilityQuery : ISeatAvailabilityQuery
{
    private readonly CinemaDbContext _db;
    private readonly TimeProvider _clock;

    public SeatAvailabilityQuery(CinemaDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<SeatAvailability> CheckAsync(int showtimeId, int seatId, CancellationToken ct = default)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        // Một round-trip: điều kiện "suất còn bán" giống BookingController.FindBookableShowtimeAsync,
        // ghế phải thuộc đúng phòng của suất, và EXISTS trên BookingSeats(ShowtimeId, SeatId) — cột
        // có unique index nên là một index seek.
        var seat = await _db.Showtimes.AsNoTracking()
            .Where(s => s.Id == showtimeId
                && s.StartTimeUtc > nowUtc
                && s.Movie!.IsActive
                && s.Room!.Theater!.IsActive)
            .SelectMany(s => s.Room!.Seats.Where(seat => seat.Id == seatId))
            .Select(seat => new
            {
                Booked = _db.BookingSeats.Any(bs => bs.ShowtimeId == showtimeId && bs.SeatId == seat.Id),
            })
            .FirstOrDefaultAsync(ct);

        if (seat is null) return SeatAvailability.NotBookable;
        return seat.Booked ? SeatAvailability.Booked : SeatAvailability.Available;
    }
}
