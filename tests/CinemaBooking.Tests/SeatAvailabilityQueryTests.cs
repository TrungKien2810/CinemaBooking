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
/// Chạy trên SQLite in-memory thay vì provider InMemory: truy vấn dùng SelectMany + EXISTS lồng,
/// cần một provider dịch ra SQL thật để bắt lỗi không dịch được.
/// </summary>
public sealed class SeatAvailabilityQueryTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private CinemaDbContext _db = null!;
    private Theater _theater = null!;
    private Showtime _showtime = null!;
    private Seat _seat = null!;
    private Seat _seatInOtherRoom = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new CinemaDbContext(new DbContextOptionsBuilder<CinemaDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();

        var room = new Room { RoomName = "Phòng 1", TotalRows = 1, TotalColumns = 2 };
        var otherRoom = new Room { RoomName = "Phòng 2", TotalRows = 1, TotalColumns = 1 };
        _theater = new Theater { Name = "Vincom Bà Triệu", City = "Hà Nội", Rooms = { room, otherRoom } };
        var movie = new Movie { Title = "Mắt Biếc", DurationMinutes = 117 };
        _db.Theaters.Add(_theater);
        _db.Movies.Add(movie);
        await _db.SaveChangesAsync();

        _db.Seats.AddRange(SeatGenerator.Generate(room));
        _db.Seats.AddRange(SeatGenerator.Generate(otherRoom));
        var start = Now.AddDays(1);
        _showtime = new Showtime { Movie = movie, Room = room, StartTimeUtc = start, EndTimeUtc = start.AddMinutes(117), BasePrice = 90_000m };
        _db.Showtimes.Add(_showtime);
        await _db.SaveChangesAsync();

        _seat = await _db.Seats.FirstAsync(s => s.RoomId == room.Id);
        _seatInOtherRoom = await _db.Seats.FirstAsync(s => s.RoomId == otherRoom.Id);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private SeatAvailabilityQuery Sut(DateTime? nowUtc = null) =>
        new(_db, new FixedTimeProvider(nowUtc ?? Now), Options.Create(new VnpayOptions()));

    [Fact]
    public async Task Free_seat_of_an_upcoming_showtime_is_available()
    {
        Assert.Equal(SeatAvailability.Available, await Sut().CheckAsync(_showtime.Id, _seat.Id));
    }

    [Fact]
    public async Task Seat_with_a_confirmed_booking_row_is_booked()
    {
        _db.Bookings.Add(new Booking
        {
            BookingCode = "CB-T00001",
            ShowtimeId = _showtime.Id,
            CustomerName = "Khách",
            CustomerEmail = "khach@example.com",
            BookingTimeUtc = Now.AddMinutes(-1),
            Status = BookingStatus.Confirmed,
            Seats = { new BookingSeat { SeatId = _seat.Id, ShowtimeId = _showtime.Id, PriceAtBooking = 90_000m } },
        });
        await _db.SaveChangesAsync();

        Assert.Equal(SeatAvailability.Booked, await Sut().CheckAsync(_showtime.Id, _seat.Id));
    }

    [Fact]
    public async Task Seat_held_by_a_booking_still_waiting_for_payment_is_booked()
    {
        _db.Bookings.Add(new Booking
        {
            BookingCode = "CB-T00002",
            ShowtimeId = _showtime.Id,
            CustomerName = "Khách",
            CustomerEmail = "khach@example.com",
            BookingTimeUtc = Now.AddMinutes(-1),
            Status = BookingStatus.Pending,
            Seats = { new BookingSeat { SeatId = _seat.Id, ShowtimeId = _showtime.Id, PriceAtBooking = 90_000m } },
        });
        await _db.SaveChangesAsync();

        Assert.Equal(SeatAvailability.Booked, await Sut().CheckAsync(_showtime.Id, _seat.Id));
    }

    [Fact]
    public async Task Seat_held_by_an_abandoned_pending_booking_is_available_again()
    {
        // Quá hạn chờ thanh toán (VNPAY 15 phút + 5 phút gia hạn): đơn chưa trả tiền không được giữ ghế.
        _db.Bookings.Add(new Booking
        {
            BookingCode = "CB-T00003",
            ShowtimeId = _showtime.Id,
            CustomerName = "Khách bỏ dở",
            CustomerEmail = "bo@example.com",
            BookingTimeUtc = Now.AddMinutes(-30),
            Status = BookingStatus.Pending,
            Seats = { new BookingSeat { SeatId = _seat.Id, ShowtimeId = _showtime.Id, PriceAtBooking = 90_000m } },
        });
        await _db.SaveChangesAsync();

        Assert.Equal(SeatAvailability.Available, await Sut().CheckAsync(_showtime.Id, _seat.Id));
    }

    [Fact]
    public async Task Seat_from_another_room_is_not_bookable()
    {
        Assert.Equal(SeatAvailability.NotBookable, await Sut().CheckAsync(_showtime.Id, _seatInOtherRoom.Id));
    }

    [Fact]
    public async Task Showtime_that_already_started_or_does_not_exist_is_not_bookable()
    {
        Assert.Equal(SeatAvailability.NotBookable, await Sut(_showtime.StartTimeUtc).CheckAsync(_showtime.Id, _seat.Id));
        Assert.Equal(SeatAvailability.NotBookable, await Sut().CheckAsync(_showtime.Id + 100, _seat.Id));
    }

    [Fact]
    public async Task Showtime_at_a_closed_branch_is_not_bookable()
    {
        _theater.IsActive = false;
        await _db.SaveChangesAsync();

        Assert.Equal(SeatAvailability.NotBookable, await Sut().CheckAsync(_showtime.Id, _seat.Id));
    }
}
