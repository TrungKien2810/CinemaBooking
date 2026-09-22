using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Hubs;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Controllers;

/// <summary>
/// Luồng đặt vé phía khách. Trang chọn ghế trả về sơ đồ + ghế đã đặt (đọc từ DB);
/// ghế ĐANG GIỮ thì client nhận qua SeatHub (SeatSnapshot), không đi qua controller.
/// </summary>
public class BookingController : Controller
{
    private readonly CinemaDbContext _db;
    private readonly IBookedSeatQuery _bookedSeats;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;

    public BookingController(
        CinemaDbContext db,
        IBookedSeatQuery bookedSeats,
        IOptions<DisplayTimeZone> tz,
        TimeProvider clock)
    {
        _db = db;
        _bookedSeats = bookedSeats;
        _tz = tz.Value;
        _clock = clock;
    }

    /// <summary>
    /// Suất không tồn tại, đã bắt đầu, hoặc phim/chi nhánh đã ẩn thì trả 404 — id trên URL do
    /// khách gõ được, không được mở trang giữ ghế cho suất không còn bán.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SeatMap(int id, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var showtime = await _db.Showtimes.AsNoTracking()
            .Include(s => s.Movie)
            .Include(s => s.Room!).ThenInclude(r => r.Theater)
            .Include(s => s.Room!).ThenInclude(r => r.Seats)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id && s.StartTimeUtc > nowUtc
                                      && s.Movie!.IsActive && s.Room!.Theater!.IsActive, ct);

        if (showtime is null) return NotFound();

        var booked = await _bookedSeats.GetBookedSeatIdsAsync(showtime.Id, ct);

        return View(SeatMapViewModel.Build(
            showtime, booked, _tz.ToLocalTime(showtime.StartTimeUtc), (int)SeatHub.HoldDuration.TotalSeconds));
    }
}
