using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

/// <summary>
/// Chốt chặn cuối chống đặt trùng (General.md §5.1 mục 2) phải được chính DB thực thi, nên chạy
/// trên SQLite in-memory — provider InMemory không kiểm unique index lẫn khóa ngoại.
/// </summary>
public sealed class BookingSeatConstraintTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private CinemaDbContext _db = null!;
    private Showtime _showtime = null!;
    private Showtime _otherShowtime = null!;
    private Seat _seat = null!;
    private int _codeNumber;

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
        _otherShowtime = new Showtime { Movie = movie, Room = room, StartTimeUtc = start.AddHours(4), EndTimeUtc = start.AddHours(6), BasePrice = 90_000m };
        _db.Showtimes.AddRange(_showtime, _otherShowtime);
        await _db.SaveChangesAsync();

        _seat = await _db.Seats.OrderBy(s => s.Id).FirstAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private Booking NewBooking(int showtimeId, int seatShowtimeId, int seatId) => new()
    {
        BookingCode = $"CB-T{++_codeNumber:00000}",
        ShowtimeId = showtimeId,
        CustomerName = "Khách",
        CustomerEmail = "khach@example.com",
        BookingTimeUtc = DateTime.UtcNow,
        Status = BookingStatus.Pending,
        Seats = { new BookingSeat { SeatId = seatId, ShowtimeId = seatShowtimeId, PriceAtBooking = 90_000m } },
    };

    [Fact]
    public async Task Second_booking_of_the_same_seat_in_the_same_showtime_is_rejected()
    {
        _db.Bookings.Add(NewBooking(_showtime.Id, _showtime.Id, _seat.Id));
        await _db.SaveChangesAsync();

        _db.Bookings.Add(NewBooking(_showtime.Id, _showtime.Id, _seat.Id));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());

        Assert.Contains("UNIQUE", ex.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
        _db.ChangeTracker.Clear();
        Assert.Equal(1, await _db.BookingSeats.CountAsync());
    }

    [Fact]
    public async Task Same_seat_can_be_booked_in_a_different_showtime()
    {
        _db.Bookings.Add(NewBooking(_showtime.Id, _showtime.Id, _seat.Id));
        _db.Bookings.Add(NewBooking(_otherShowtime.Id, _otherShowtime.Id, _seat.Id));

        await _db.SaveChangesAsync();

        Assert.Equal(2, await _db.BookingSeats.CountAsync(bs => bs.SeatId == _seat.Id));
    }

    [Fact]
    public async Task Booking_seat_pointing_to_a_missing_showtime_is_rejected()
    {
        _db.Bookings.Add(NewBooking(_showtime.Id, seatShowtimeId: 9999, _seat.Id));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());

        Assert.Contains("FOREIGN KEY", ex.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Booked_seat_query_returns_exactly_the_seats_the_index_would_reject()
    {
        _db.Bookings.Add(NewBooking(_showtime.Id, _showtime.Id, _seat.Id));
        _db.Bookings.Add(NewBooking(_otherShowtime.Id, _otherShowtime.Id, _seat.Id + 1));
        await _db.SaveChangesAsync();

        var booked = await new BookedSeatQuery(_db, TimeProvider.System, Options.Create(new VnpayOptions()))
            .GetBookedSeatIdsAsync(_showtime.Id);

        Assert.Equal([_seat.Id], booked);
    }
}
