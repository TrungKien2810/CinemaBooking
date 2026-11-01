using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

/// <summary>
/// Hủy vé phía khách: quyền sở hữu, hạn hủy và đường hoàn tiền. Chạy trên SQLite để
/// transaction + unique index (ShowtimeId, SeatId) hoạt động như DB thật.
/// </summary>
public sealed class CustomerBookingCancellationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 31, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ShowtimeStart = new(2026, 10, 31, 18, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private CinemaDbContext _db = null!;
    private ApplicationUser _customer = null!;
    private Showtime _showtime = null!;
    private List<Seat> _seats = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new CinemaDbContext(new DbContextOptionsBuilder<CinemaDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();

        _customer = new ApplicationUser
        {
            Id = "customer-1",
            UserName = "customer@example.com",
            NormalizedUserName = "CUSTOMER@EXAMPLE.COM",
            Email = "customer@example.com",
            NormalizedEmail = "CUSTOMER@EXAMPLE.COM",
            FullName = "Khách Hàng",
            EmailConfirmed = true,
        };
        _db.Users.Add(_customer);

        var room = new Room { RoomName = "Phòng 1", TotalRows = 2, TotalColumns = 2 };
        var movie = new Movie { Title = "Mắt Biếc", DurationMinutes = 117 };
        _db.Theaters.Add(new Theater { Name = "CineSmart", City = "Hà Nội", Rooms = { room } });
        _db.Movies.Add(movie);
        await _db.SaveChangesAsync();

        _db.Seats.AddRange(SeatGenerator.Generate(room));
        _showtime = new Showtime
        {
            Movie = movie,
            Room = room,
            StartTimeUtc = ShowtimeStart,
            EndTimeUtc = ShowtimeStart.AddMinutes(117),
            BasePrice = 90_000m,
        };
        _db.Showtimes.Add(_showtime);
        await _db.SaveChangesAsync();

        _seats = await _db.Seats.OrderBy(s => s.Id).ToListAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Owner_can_cancel_and_mock_refund_frees_the_seats()
    {
        var booking = await AddBookingAsync("CB-CUST01", userId: _customer.Id);

        var result = await Service().CancelAsync(new CancelBookingRequest(
            booking.BookingCode, _customer.Id, _customer.Email, IsAdmin: false, Actor: "customer:1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(180_000m, result.RefundedAmount);
        Assert.Equal(2, result.SeatIds!.Count);

        _db.ChangeTracker.Clear();
        var saved = await _db.Bookings.Include(b => b.Seats).Include(b => b.Payment)
            .SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Cancelled, saved.Status);
        Assert.Empty(saved.Seats);
        Assert.Equal(PaymentStatus.Refunded, saved.Payment!.Status);
        Assert.Equal(180_000m, saved.Payment.RefundedAmount);
        Assert.Equal("MOCK", saved.Payment.RefundResponseCode);
        Assert.Single(await _db.OutboxEmails.Where(e => e.ToAddress == _customer.Email).ToListAsync());
    }

    [Fact]
    public async Task Another_account_cannot_cancel_the_booking()
    {
        var booking = await AddBookingAsync("CB-CUST01", userId: _customer.Id);

        var result = await Service().CancelAsync(new CancelBookingRequest(
            booking.BookingCode, "other-user", "other@example.com", IsAdmin: false, Actor: "customer:other"));

        Assert.Equal(CancelBookingOutcome.NotOwner, result.Outcome);
        _db.ChangeTracker.Clear();
        Assert.Equal(BookingStatus.Confirmed, (await _db.Bookings.SingleAsync(b => b.Id == booking.Id)).Status);
        Assert.Equal(2, await _db.BookingSeats.CountAsync(bs => bs.BookingId == booking.Id));
    }

    [Fact]
    public async Task Customer_cannot_cancel_within_the_configured_cutoff()
    {
        var booking = await AddBookingAsync("CB-CUST01", userId: _customer.Id);

        // 17:00, suất 18:00 — còn đúng 1 giờ, dưới hạn 2 giờ.
        var result = await Service(now: new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc))
            .CancelAsync(new CancelBookingRequest(
                booking.BookingCode, _customer.Id, _customer.Email, IsAdmin: false, Actor: "customer:1"));

        Assert.Equal(CancelBookingOutcome.TooLate, result.Outcome);
        _db.ChangeTracker.Clear();
        Assert.Equal(BookingStatus.Confirmed, (await _db.Bookings.SingleAsync(b => b.Id == booking.Id)).Status);
    }

    [Fact]
    public async Task Guest_can_cancel_with_the_email_used_to_book()
    {
        var booking = await AddBookingAsync("CB-GUEST1", userId: null);

        var result = await Service().CancelAsync(new CancelBookingRequest(
            booking.BookingCode, UserId: null, Email: "guest@example.com", IsAdmin: false, Actor: "guest:guest@example.com"));

        Assert.True(result.IsSuccess);
        _db.ChangeTracker.Clear();
        Assert.Equal(BookingStatus.Cancelled, (await _db.Bookings.SingleAsync(b => b.Id == booking.Id)).Status);
    }

    [Fact]
    public async Task Guest_cannot_cancel_with_a_different_email()
    {
        var booking = await AddBookingAsync("CB-GUEST1", userId: null);

        var result = await Service().CancelAsync(new CancelBookingRequest(
            booking.BookingCode, UserId: null, Email: "wrong@example.com", IsAdmin: false, Actor: "guest:wrong@example.com"));

        Assert.Equal(CancelBookingOutcome.NotOwner, result.Outcome);
        _db.ChangeTracker.Clear();
        Assert.Equal(BookingStatus.Confirmed, (await _db.Bookings.SingleAsync(b => b.Id == booking.Id)).Status);
    }

    [Fact]
    public async Task A_failed_vnpay_refund_keeps_the_booking_confirmed_so_it_can_be_retried()
    {
        var booking = await AddBookingAsync("CB-CUST01", userId: _customer.Id);
        var refund = new FakeRefundService(
            new VnpayRefundResult(false, "99", "02", null, "VNPAY tạm lỗi"),
            new VnpayRefundResult(true, "00", "00", "REFUND-OK", "OK"));

        var first = await Service(refund: refund, refundEnabled: true).CancelAsync(new CancelBookingRequest(
            booking.BookingCode, _customer.Id, _customer.Email, IsAdmin: false, Actor: "customer:1"));

        Assert.Equal(CancelBookingOutcome.RefundFailed, first.Outcome);
        _db.ChangeTracker.Clear();
        var stillHeld = await _db.Bookings.Include(b => b.Seats).Include(b => b.Payment)
            .SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Confirmed, stillHeld.Status);
        Assert.Equal(2, stillHeld.Seats.Count);
        Assert.Equal(PaymentStatus.RefundFailed, stillHeld.Payment!.Status);

        // Lần thứ hai VNPAY nhận: đơn mới thực sự bị hủy và hoàn tiền.
        var second = await Service(refund: refund, refundEnabled: true).CancelAsync(new CancelBookingRequest(
            booking.BookingCode, _customer.Id, _customer.Email, IsAdmin: false, Actor: "customer:1"));

        Assert.True(second.IsSuccess);
        _db.ChangeTracker.Clear();
        var done = await _db.Bookings.Include(b => b.Seats).Include(b => b.Payment)
            .SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Cancelled, done.Status);
        Assert.Empty(done.Seats);
        Assert.Equal(PaymentStatus.Refunded, done.Payment!.Status);
        Assert.Equal("REFUND-OK", done.Payment.RefundTransactionNo);
    }

    [Fact]
    public async Task Repeated_refund_failures_do_not_leave_the_payment_stuck_in_pending()
    {
        var booking = await AddBookingAsync("CB-CUST01", userId: _customer.Id);
        var refund = new FakeRefundService(
            new VnpayRefundResult(false, "99", "02", null, "VNPAY tạm lỗi"),
            new VnpayRefundResult(false, "99", "02", null, "VNPAY vẫn lỗi"),
            new VnpayRefundResult(true, "00", "00", "REFUND-OK", "OK"));

        var first = await Service(refund: refund, refundEnabled: true).CancelAsync(new CancelBookingRequest(
            booking.BookingCode, _customer.Id, _customer.Email, IsAdmin: false, Actor: "customer:1"));
        Assert.Equal(CancelBookingOutcome.RefundFailed, first.Outcome);
        _db.ChangeTracker.Clear();

        // Lần thử lại cũng lỗi: trạng thái DB phải là RefundFailed, không được kẹt RefundPending.
        var second = await Service(refund: refund, refundEnabled: true).CancelAsync(new CancelBookingRequest(
            booking.BookingCode, _customer.Id, _customer.Email, IsAdmin: false, Actor: "customer:1"));
        Assert.Equal(CancelBookingOutcome.RefundFailed, second.Outcome);
        _db.ChangeTracker.Clear();

        var afterFailures = await _db.Payments.SingleAsync(p => p.BookingId == booking.Id);
        Assert.Equal(PaymentStatus.RefundFailed, afterFailures.Status);

        var third = await Service(refund: refund, refundEnabled: true).CancelAsync(new CancelBookingRequest(
            booking.BookingCode, _customer.Id, _customer.Email, IsAdmin: false, Actor: "customer:1"));
        Assert.True(third.IsSuccess);
        _db.ChangeTracker.Clear();
        Assert.Equal(PaymentStatus.Refunded,
            (await _db.Payments.SingleAsync(p => p.BookingId == booking.Id)).Status);
    }

    [Fact]
    public async Task Real_refund_uses_the_original_vnpay_transaction_fields()
    {
        var booking = await AddBookingAsync("CB-CUST01", userId: _customer.Id);
        var refund = new FakeRefundService(new VnpayRefundResult(true, "00", "00", "REFUND-OK", "OK"));

        var result = await Service(refund: refund, refundEnabled: true).CancelAsync(new CancelBookingRequest(
            booking.BookingCode, _customer.Id, _customer.Email, IsAdmin: false, Actor: "customer:1"));

        Assert.True(result.IsSuccess);
        var request = Assert.Single(refund.Requests);
        Assert.Equal(booking.BookingCode, request.TxnRef);
        Assert.Equal(180_000m, request.Amount);
        Assert.Equal("123456", request.TransactionNo);
        Assert.Equal("20261031100000", request.TransactionDate);
        Assert.True(request.FullRefund);
        Assert.Matches("^RF[0-9A-F]+$", request.RequestId);

        _db.ChangeTracker.Clear();
        var payment = await _db.Payments.SingleAsync(p => p.BookingId == booking.Id);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal("REFUND-OK", payment.RefundTransactionNo);
        Assert.Equal(request.RequestId, payment.RefundRequestId);
        Assert.Equal("00", payment.RefundResponseCode);
    }

    private async Task<Booking> AddBookingAsync(string code, string? userId)
    {
        var email = userId is null ? "guest@example.com" : _customer.Email!;
        var booking = new Booking
        {
            BookingCode = code,
            UserId = userId,
            ShowtimeId = _showtime.Id,
            CustomerName = "Khách",
            CustomerEmail = email,
            BookingTimeUtc = Now.AddMinutes(-10),
            Status = BookingStatus.Confirmed,
            TotalAmount = 180_000m,
            Payment = new Payment
            {
                Amount = 180_000m,
                PaymentMethod = "VNPAY",
                Status = PaymentStatus.Succeeded,
                TransactionId = "123456",
                VnpayCreateDate = "20261031100000",
                VnpayPayDate = "20261031100500",
                PaidUtc = Now.AddMinutes(-5),
                CreatedUtc = Now.AddMinutes(-10),
            },
        };

        foreach (var seat in _seats.Take(2))
        {
            booking.Seats.Add(new BookingSeat
            {
                SeatId = seat.Id,
                ShowtimeId = _showtime.Id,
                PriceAtBooking = 90_000m,
            });
        }

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();
        return booking;
    }

    private BookingCancellationService Service(
        FakeRefundService? refund = null,
        bool refundEnabled = false,
        DateTime? now = null) =>
        new(
            _db,
            refund ?? new FakeRefundService(),
            Options.Create(new VnpayOptions
            {
                RefundEnabled = refundEnabled,
                CreateBy = "test@example.com",
                IpAddr = "127.0.0.1",
            }),
            Options.Create(new BookingCancellationOptions
            {
                MinHoursBeforeShowtime = 2,
                AllowGuestCancel = true,
            }),
            new DisplayTimeZone(),
            new FixedTimeProvider(now ?? Now),
            NullLogger<BookingCancellationService>.Instance);

    private sealed class FakeRefundService : IVnpayRefundService
    {
        private readonly Queue<VnpayRefundResult> _results;

        public FakeRefundService(params VnpayRefundResult[] results) => _results = new Queue<VnpayRefundResult>(results);

        public List<VnpayRefundRequest> Requests { get; } = new();

        public Task<VnpayRefundResult> RefundAsync(VnpayRefundRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(_results.Count > 0
                ? _results.Dequeue()
                : new VnpayRefundResult(true, "00", "00", "REFUND-TEST", "OK"));
        }
    }
}
