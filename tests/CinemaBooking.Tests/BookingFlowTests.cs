using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Infrastructure.Services;
using CinemaBooking.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Tests;

/// <summary>
/// Luồng đặt vé end-to-end trên SQLite in-memory (có unique index thật, khác provider InMemory).
/// Phủ bốn nhánh của <see cref="BookingService"/>: thành công, hết hạn giữ ghế, ghế đã bán, và
/// cuộc đua lọt tới unique index (ShowtimeId, SeatId).
/// </summary>
public sealed class BookingFlowTests : IAsyncLifetime
{
    private const string Connection = "conn-test";
    private static readonly DateTime Now = new(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly InMemorySeatLockManager _locks = new();

    private CinemaDbContext _db = null!;
    private Room _room = null!;
    private Showtime _showtime = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new CinemaDbContext(new DbContextOptionsBuilder<CinemaDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();

        var theater = new Theater { Name = "CineSmart", City = "Hà Nội" };
        _room = new Room { Theater = theater, RoomName = "Phòng 1", TotalRows = 3, TotalColumns = 4 };
        var movie = new Movie { Title = "Mắt Biếc", DurationMinutes = 117 };
        _db.Theaters.Add(theater);
        _db.Rooms.Add(_room);
        _db.Movies.Add(movie);
        await _db.SaveChangesAsync();

        _db.Seats.AddRange(SeatGenerator.Generate(_room));
        _showtime = new Showtime
        {
            Movie = movie,
            Room = _room,
            StartTimeUtc = Now.AddHours(2),
            EndTimeUtc = Now.AddHours(4),
            BasePrice = 90_000m,
        };
        _db.Showtimes.Add(_showtime);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Successful_booking_confirms_charges_writes_outbox_and_releases_the_lock()
    {
        var seats = await StandardSeatIdsAsync(2);
        Hold(seats);

        var result = await Service().PlaceAsync(Request(seats));

        Assert.True(result.IsSuccess);
        Assert.StartsWith(BookingCodeGenerator.Prefix, result.BookingCode);

        _db.ChangeTracker.Clear();
        var booking = await _db.Bookings
            .Include(b => b.Seats).Include(b => b.Payment)
            .SingleAsync(b => b.BookingCode == result.BookingCode);

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(2, booking.Seats.Count);
        Assert.Equal(180_000m, booking.TotalAmount);
        Assert.Equal(PaymentStatus.Succeeded, booking.Payment!.Status);
        Assert.False(string.IsNullOrEmpty(booking.Payment.TransactionId));

        var mail = await _db.OutboxEmails.SingleAsync();
        Assert.Equal(OutboxStatus.Pending, mail.Status);
        Assert.Equal(booking.CustomerEmail, mail.ToAddress);
        Assert.Contains(result.BookingCode!, mail.Subject);

        Assert.Empty(_locks.GetLocksForShowtime(_showtime.Id));
    }

    [Fact]
    public async Task Booking_without_a_held_lock_is_refused_and_writes_nothing()
    {
        var seats = await StandardSeatIdsAsync(1);

        var result = await Service().PlaceAsync(Request(seats));

        Assert.Equal(BookingResultKind.LocksExpired, result.Kind);
        Assert.Empty(await _db.Bookings.ToListAsync());
    }

    [Fact]
    public async Task Seat_already_booked_is_refused_before_touching_the_database()
    {
        var seat = (await StandardSeatIdsAsync(1))[0];
        await SeedConfirmedBookingAsync(seat);
        Hold(new[] { seat });

        var result = await Service().PlaceAsync(Request(new[] { seat }));

        Assert.Equal(BookingResultKind.SeatsUnavailable, result.Kind);
        Assert.Equal(1, await _db.Bookings.CountAsync());
    }

    [Fact]
    public async Task Losing_the_race_to_the_unique_index_is_reported_as_a_friendly_error()
    {
        var seat = (await StandardSeatIdsAsync(1))[0];
        await SeedConfirmedBookingAsync(seat);
        Hold(new[] { seat });

        // Query "đã đặt" trả rỗng -> bỏ qua kiểm tra sớm, buộc DB phải từ chối ở unique index.
        var result = await Service(bookedSeats: new NoBookedSeats()).PlaceAsync(Request(new[] { seat }));

        Assert.Equal(BookingResultKind.SeatsUnavailable, result.Kind);
        Assert.Equal(1, await _db.Bookings.CountAsync());
        Assert.Equal(1, await _db.BookingSeats.CountAsync());
    }

    [Fact]
    public async Task Payment_failure_cancels_the_booking_and_frees_its_seats()
    {
        var seats = await StandardSeatIdsAsync(2);
        Hold(seats);

        var result = await Service(payment: new FakePaymentService(succeed: false)).PlaceAsync(Request(seats));

        Assert.Equal(BookingResultKind.PaymentFailed, result.Kind);

        _db.ChangeTracker.Clear();
        var booking = await _db.Bookings.Include(b => b.Payment).SingleAsync();
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(PaymentStatus.Failed, booking.Payment!.Status);
        Assert.Empty(await _db.BookingSeats.ToListAsync());
        Assert.Empty(await _db.OutboxEmails.ToListAsync());
    }

    private BookingService Service(
        IBookedSeatQuery? bookedSeats = null,
        IPaymentService? payment = null) =>
        new(_db,
            bookedSeats ?? new BookedSeatQuery(_db),
            _locks,
            new SeatPricingService(),
            payment ?? new FakePaymentService(succeed: true),
            new BookingCodeGenerator(),
            new FixedTimeProvider(Now));

    private BookingRequest Request(IReadOnlyList<int> seatIds) =>
        new(_showtime.Id, seatIds, Connection, "Nguyễn Văn A", "a@example.com", "0900000000", null);

    private void Hold(IEnumerable<int> seatIds)
    {
        foreach (var seatId in seatIds)
            _locks.TryLock(new SeatKey(_showtime.Id, seatId), Connection, null, TimeSpan.FromMinutes(5));
    }

    private Task<List<int>> StandardSeatIdsAsync(int count) =>
        _db.Seats.Where(s => s.RoomId == _room.Id && s.RowNumber == 1)
            .OrderBy(s => s.ColumnNumber).Select(s => s.Id).Take(count).ToListAsync();

    private async Task SeedConfirmedBookingAsync(int seatId)
    {
        _db.Bookings.Add(new Booking
        {
            BookingCode = "CB-SEED01",
            ShowtimeId = _showtime.Id,
            CustomerName = "Người khác",
            CustomerEmail = "khac@example.com",
            BookingTimeUtc = Now,
            TotalAmount = 90_000m,
            Status = BookingStatus.Confirmed,
            Seats = { new BookingSeat { SeatId = seatId, ShowtimeId = _showtime.Id, PriceAtBooking = 90_000m } },
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private sealed class FakePaymentService : IPaymentService
    {
        private readonly bool _succeed;
        public FakePaymentService(bool succeed) => _succeed = succeed;

        public Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct = default) =>
            Task.FromResult(_succeed
                ? new PaymentResult(true, "TX-TEST", null)
                : new PaymentResult(false, null, "Thẻ bị từ chối."));
    }

    private sealed class NoBookedSeats : IBookedSeatQuery
    {
        public Task<IReadOnlySet<int>> GetBookedSeatIdsAsync(int showtimeId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
    }
}
