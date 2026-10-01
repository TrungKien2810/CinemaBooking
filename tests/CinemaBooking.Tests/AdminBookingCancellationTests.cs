using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Controllers;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

/// <summary>
/// Hủy vé phải thật sự trả ghế: chạy trên SQLite để unique index (ShowtimeId, SeatId) được DB thực
/// thi — ghế chỉ "trống" khi khách khác ghi được dòng BookingSeats mới cho nó (AD-5).
/// </summary>
public sealed class AdminBookingCancellationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private CinemaDbContext _db = null!;
    private Showtime _showtime = null!;
    private List<Seat> _seats = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new CinemaDbContext(new DbContextOptionsBuilder<CinemaDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();

        var room = new Room { RoomName = "Phòng 1", TotalRows = 2, TotalColumns = 2 };
        var movie = new Movie { Title = "Mắt Biếc", DurationMinutes = 117 };
        _db.Theaters.Add(new Theater { Name = "CineSmart", City = "Hà Nội", Rooms = { room } });
        _db.Movies.Add(movie);
        await _db.SaveChangesAsync();

        _db.Seats.AddRange(SeatGenerator.Generate(room));
        var start = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        _showtime = new Showtime { Movie = movie, Room = room, StartTimeUtc = start, EndTimeUtc = start.AddMinutes(117), BasePrice = 90_000m };
        _db.Showtimes.Add(_showtime);
        await _db.SaveChangesAsync();

        _seats = await _db.Seats.OrderBy(s => s.Id).ToListAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<Booking> AddBookingAsync(string code, BookingStatus status, PaymentStatus? payment, params Seat[] seats)
    {
        var booking = new Booking
        {
            BookingCode = code,
            ShowtimeId = _showtime.Id,
            CustomerName = "Khách",
            CustomerEmail = "khach@example.com",
            Status = status,
            TotalAmount = 90_000m * seats.Length,
            Payment = payment is null ? null : new Payment { Amount = 90_000m * seats.Length, Status = payment.Value },
        };
        foreach (var seat in seats)
            booking.Seats.Add(new BookingSeat { SeatId = seat.Id, ShowtimeId = _showtime.Id, PriceAtBooking = 90_000m });
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();
        return booking;
    }

    private BookingsController CreateController() => new(_db, Options.Create(new DisplayTimeZone()))
    {
        TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider()),
    };

    [Fact]
    public async Task Cancel_frees_the_seats_and_refunds_a_paid_booking()
    {
        var booking = await AddBookingAsync("CB-000001", BookingStatus.Confirmed, PaymentStatus.Succeeded, _seats[0], _seats[1]);
        var other = await AddBookingAsync("CB-000002", BookingStatus.Confirmed, PaymentStatus.Succeeded, _seats[2]);

        var result = await CreateController().Cancel(booking.Id, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        _db.ChangeTracker.Clear();
        var saved = await _db.Bookings.Include(b => b.Seats).Include(b => b.Payment).SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Cancelled, saved.Status);
        Assert.Empty(saved.Seats);
        Assert.Equal(PaymentStatus.Refunded, saved.Payment!.Status);
        // Đơn khác của cùng suất không bị đụng tới.
        Assert.Equal(1, await _db.BookingSeats.CountAsync(bs => bs.BookingId == other.Id));
    }

    [Fact]
    public async Task A_cancelled_seat_can_be_booked_again()
    {
        var booking = await AddBookingAsync("CB-000001", BookingStatus.Confirmed, PaymentStatus.Succeeded, _seats[0]);

        await CreateController().Cancel(booking.Id, CancellationToken.None);
        _db.ChangeTracker.Clear();

        // Không xóa dòng BookingSeats thì lần ghi này vi phạm unique index và ném DbUpdateException.
        var rebooked = await AddBookingAsync("CB-000002", BookingStatus.Pending, null, _seats[0]);
        Assert.Equal(1, await _db.BookingSeats.CountAsync(bs => bs.BookingId == rebooked.Id));
    }

    [Fact]
    public async Task Cancel_leaves_an_unpaid_booking_without_a_refund()
    {
        var booking = await AddBookingAsync("CB-000001", BookingStatus.Pending, PaymentStatus.Pending, _seats[0]);

        await CreateController().Cancel(booking.Id, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var payment = await _db.Payments.SingleAsync(p => p.BookingId == booking.Id);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Theory]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Expired)]
    public async Task Cancel_rejects_a_booking_that_no_longer_holds_seats(BookingStatus status)
    {
        var booking = await AddBookingAsync("CB-000001", status, PaymentStatus.Refunded);
        var controller = CreateController();

        await controller.Cancel(booking.Id, CancellationToken.None);

        Assert.True(controller.TempData.ContainsKey("Error"));
        _db.ChangeTracker.Clear();
        Assert.Equal(status, (await _db.Bookings.SingleAsync(b => b.Id == booking.Id)).Status);
    }

    [Fact]
    public async Task Cancel_returns_not_found_for_an_unknown_booking()
    {
        Assert.IsType<NotFoundResult>(await CreateController().Cancel(999, CancellationToken.None));
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
