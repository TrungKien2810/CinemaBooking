using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Vé còn hiệu lực (đã thanh toán hoặc đang chờ thanh toán). Khu quản trị dùng để chặn những thao tác làm sai
/// lời hứa với khách đã mua vé: đổi phim/giờ của suất, ẩn phim, đóng chi nhánh.
/// </summary>
public static class SoldTickets
{
    private static IQueryable<Booking> Active(CinemaDbContext db) =>
        db.Bookings.Where(b => b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Pending);

    public static Task<int> ForShowtimeAsync(CinemaDbContext db, int showtimeId, CancellationToken ct = default) =>
        Active(db).CountAsync(b => b.ShowtimeId == showtimeId, ct);

    /// <summary>Đơn còn hiệu lực của các suất CHƯA chiếu của phim này.</summary>
    public static Task<int> UpcomingForMovieAsync(CinemaDbContext db, int movieId, DateTime nowUtc, CancellationToken ct = default) =>
        Active(db).CountAsync(b => b.Showtime!.MovieId == movieId && b.Showtime.StartTimeUtc > nowUtc, ct);

    /// <summary>Đơn còn hiệu lực của các suất CHƯA chiếu ở chi nhánh này.</summary>
    public static Task<int> UpcomingForTheaterAsync(CinemaDbContext db, int theaterId, DateTime nowUtc, CancellationToken ct = default) =>
        Active(db).CountAsync(b => b.Showtime!.Room!.TheaterId == theaterId && b.Showtime.StartTimeUtc > nowUtc, ct);
}
