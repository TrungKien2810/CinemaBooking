using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Services;

public sealed class SeatAvailabilityQuery : ISeatAvailabilityQuery
{
    private readonly CinemaDbContext _db;
    private readonly TimeProvider _clock;
    private readonly VnpayOptions _vnpay;

    public SeatAvailabilityQuery(CinemaDbContext db, TimeProvider clock, IOptions<VnpayOptions> vnpay)
    {
        _db = db;
        _clock = clock;
        _vnpay = vnpay.Value;
    }

    public async Task<SeatAvailability> CheckAsync(int showtimeId, int seatId, CancellationToken ct = default)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var pendingCutoff = nowUtc - _vnpay.PendingHoldWindow;

        // Một round-trip: điều kiện "suất còn bán" giống BookingController.FindBookableShowtimeAsync,
        // ghế phải thuộc đúng phòng của suất, và EXISTS trên BookingSeats(ShowtimeId, SeatId) — cột
        // có unique index nên là một index seek.
        var seat = await _db.Showtimes.AsNoTracking()
            .Where(s => s.Id == showtimeId
                && s.StartTimeUtc > nowUtc
                && s.IsActive
                && s.Movie!.IsActive
                && s.Room!.Theater!.IsActive)
            .SelectMany(s => s.Room!.Seats.Where(seat => seat.Id == seatId))
            .Select(seat => new
            {
                // Đơn Pending quá hạn chờ thanh toán không còn giữ ghế (xem BookedSeatQuery).
                Booked = _db.BookingSeats.Any(bs => bs.ShowtimeId == showtimeId && bs.SeatId == seat.Id
                    && (bs.Booking!.Status == BookingStatus.Confirmed
                        || (bs.Booking.Status == BookingStatus.Pending && bs.Booking.BookingTimeUtc > pendingCutoff))),
            })
            .FirstOrDefaultAsync(ct);

        if (seat is null) return SeatAvailability.NotBookable;
        return seat.Booked ? SeatAvailability.Booked : SeatAvailability.Available;
    }
}
