using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Infrastructure.Services;
using CinemaBooking.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CinemaBooking.Tests;

/// <summary>
/// Luồng đặt vé end-to-end trên SQLite in-memory (có unique index thật, khác provider InMemory).
/// Phủ bốn nhánh của <see cref="BookingService"/>: thành công, hết hạn giữ ghế, ghế đã bán, và
/// cuộc đua lọt tới unique index (ShowtimeId, SeatId).
/// </summary>
public sealed class BookingFlowTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Filename=:memory:");

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
    public async Task Successful_booking_confirms_charges_and_writes_outbox()
    {
        var seats = await StandardSeatIdsAsync(2);

        // Luồng VNPAY: PlaceAsync tạo Booking Pending + URL thanh toán rồi chuyển sang cổng VNPAY;
        // sau đó VNPAY trả về (ReturnUrl/IPN) xác nhận giao dịch.
        var result = await Service().PlaceAsync(Request(seats));

        Assert.True(result.IsRedirectToPayment);
        Assert.StartsWith(BookingCodeGenerator.Prefix, result.BookingCode);

        var bookingCode = result.BookingCode!;
        var confirm = await Service().ConfirmAsync(bookingCode, new VnpayVerifyResult(
            SignatureValid: true,
            PaymentSucceeded: true,
            TxnRef: bookingCode,
            ResponseCode: "00",
            TransactionId: "VNPAY-TEST",
            BankCode: "NCB",
            Amount: 180_000m,
            FailureReason: null));

        Assert.True(confirm.IsSuccess);
        Assert.Equal(BookingResultKind.Success, confirm.Kind);

        _db.ChangeTracker.Clear();
        var booking = await _db.Bookings
            .Include(b => b.Seats).Include(b => b.Payment)
            .SingleAsync(b => b.BookingCode == bookingCode);

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(2, booking.Seats.Count);
        Assert.Equal(180_000m, booking.TotalAmount);
        Assert.Equal(PaymentStatus.Succeeded, booking.Payment!.Status);
        Assert.Equal("VNPAY", booking.Payment.PaymentMethod);
        Assert.False(string.IsNullOrEmpty(booking.Payment.TransactionId));

        var mail = await _db.OutboxEmails.SingleAsync();
        Assert.Equal(OutboxStatus.Pending, mail.Status);
        Assert.Equal(booking.CustomerEmail, mail.ToAddress);
        Assert.Contains(bookingCode, mail.Subject);
    }

    [Fact]
    public async Task Booking_needs_no_prior_hold_and_two_orders_for_one_seat_leave_the_second_refused_before_payment()
    {
        var seat = (await StandardSeatIdsAsync(1))[0];
        var vnpay = new CountingVnpay();

        var first = await Service(vnpay: vnpay).PlaceAsync(Request(new[] { seat }, "a@example.com"));
        Assert.True(first.IsRedirectToPayment);
        Assert.Equal(1, vnpay.Calls);

        // Người thứ hai (email khác) chọn cùng ghế: ghế đã bị đơn Pending của người thứ nhất chặn,
        // bị từ chối TRƯỚC khi gọi VNPAY nên không có đồng tiền nào bị trừ.
        var second = await Service(vnpay: vnpay).PlaceAsync(Request(new[] { seat }, "b@example.com"));

        Assert.Equal(BookingResultKind.SeatsUnavailable, second.Kind);
        Assert.Equal(1, vnpay.Calls);
        Assert.Equal(1, await _db.Bookings.CountAsync());
    }

    [Fact]
    public async Task A_second_pending_order_from_the_same_email_is_refused_case_insensitively()
    {
        var seats = await StandardSeatIdsAsync(2);
        Assert.True((await Service().PlaceAsync(Request(new[] { seats[0] }, "Khach@Example.com"))).IsRedirectToPayment);

        var again = await Service().PlaceAsync(Request(new[] { seats[1] }, "khach@example.COM"));

        Assert.Equal(BookingResultKind.SeatsUnavailable, again.Kind);
        Assert.Contains("đơn chờ thanh toán", again.Message);
        Assert.Equal(1, await _db.Bookings.CountAsync());
    }

    [Fact]
    public async Task A_second_pending_order_from_the_same_signed_in_user_is_refused_even_with_another_email()
    {
        _db.Users.Add(new ApplicationUser { Id = "user-1", UserName = "user-1" });
        await _db.SaveChangesAsync();
        var seats = await StandardSeatIdsAsync(2);
        Assert.True((await Service().PlaceAsync(Request(new[] { seats[0] }, "x@example.com", userId: "user-1"))).IsRedirectToPayment);

        var again = await Service().PlaceAsync(Request(new[] { seats[1] }, "y@example.com", userId: "user-1"));

        Assert.Equal(BookingResultKind.SeatsUnavailable, again.Kind);
    }

    [Fact]
    public async Task Same_email_may_order_again_once_the_first_pending_order_is_cancelled_or_past_the_window()
    {
        var seats = await StandardSeatIdsAsync(3);
        var first = await Service().PlaceAsync(Request(new[] { seats[0] }));
        Assert.True(first.IsRedirectToPayment);

        // Đơn đầu bị huỷ (khách huỷ ở cổng VNPAY): đặt lại được ngay.
        await Service().ConfirmAsync(first.BookingCode!, new VnpayVerifyResult(
            true, false, first.BookingCode, "24", null, null, 90_000m, "Khách hủy thanh toán."));
        var second = await Service().PlaceAsync(Request(new[] { seats[1] }));
        Assert.True(second.IsRedirectToPayment);

        // Đơn Pending thứ hai bỏ dở quá cửa sổ giữ (10 + 2 phút): lại đặt được.
        _db.ChangeTracker.Clear();
        var stale = await _db.Bookings.SingleAsync(b => b.BookingCode == second.BookingCode);
        stale.BookingTimeUtc = Now.AddMinutes(-13);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.True((await Service().PlaceAsync(Request(new[] { seats[2] }))).IsRedirectToPayment);
    }

    [Fact]
    public async Task More_than_eight_seats_in_one_order_is_refused_and_writes_nothing()
    {
        var seats = await _db.Seats.Where(s => s.RoomId == _room.Id).OrderBy(s => s.RowNumber).ThenBy(s => s.ColumnNumber)
            .Select(s => s.Id).Take(9).ToListAsync();
        Assert.Equal(9, seats.Count);

        var result = await Service().PlaceAsync(Request(seats));

        Assert.Equal(BookingResultKind.SeatsUnavailable, result.Kind);
        Assert.Contains("tối đa 8", result.Message);
        Assert.Empty(await _db.Bookings.ToListAsync());
    }

    [Fact]
    public async Task Seat_already_booked_is_refused_before_touching_the_database()
    {
        var seat = (await StandardSeatIdsAsync(1))[0];
        await SeedConfirmedBookingAsync(seat);

        var result = await Service().PlaceAsync(Request(new[] { seat }));

        Assert.Equal(BookingResultKind.SeatsUnavailable, result.Kind);
        Assert.Equal(1, await _db.Bookings.CountAsync());
    }

    [Fact]
    public async Task Losing_the_race_to_the_unique_index_is_reported_as_a_friendly_error()
    {
        var seat = (await StandardSeatIdsAsync(1))[0];
        await SeedConfirmedBookingAsync(seat);

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

        var result = await Service(vnpay: new FakeVnpayPaymentService(succeed: false)).PlaceAsync(Request(seats));

        Assert.Equal(BookingResultKind.PaymentFailed, result.Kind);

        _db.ChangeTracker.Clear();
        var booking = await _db.Bookings.Include(b => b.Payment).SingleAsync();
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(PaymentStatus.Failed, booking.Payment!.Status);
        Assert.Empty(await _db.BookingSeats.ToListAsync());
        Assert.Empty(await _db.OutboxEmails.ToListAsync());
    }

    [Fact]
    public async Task Half_a_couple_pair_is_rejected_but_the_full_pair_books()
    {
        // Hàng cuối của phòng là ghế đôi (SeatGenerator): C1–C2, C3–C4.
        var couple = await _db.Seats
            .Where(s => s.RoomId == _room.Id && s.SeatType == SeatType.Couple)
            .OrderBy(s => s.ColumnNumber)
            .Select(s => s.Id)
            .ToListAsync();

        var half = await Service().PlaceAsync(Request(new[] { couple[0] }));

        Assert.Equal(BookingResultKind.SeatsUnavailable, half.Kind);
        Assert.Contains("cả cặp", half.Message);

        var full = await Service().PlaceAsync(Request(couple.Take(2).ToList()));

        Assert.True(full.IsRedirectToPayment);
    }

    [Fact]
    public async Task Unconfigured_vnpay_simulates_payment_so_checkout_can_finish_offline()
    {
        var seats = await StandardSeatIdsAsync(2);

        // Máy demo chưa có Vnpay:HashSecret — cổng thanh toán trả lỗi cấu hình, nhưng cờ mô phỏng
        // được bật nên đơn phải được xác nhận luôn thay vì chặn khách ở bước cuối.
        var options = new VnpayOptions { SimulateWhenUnconfigured = true, HashSecret = string.Empty };
        var result = await Service(vnpay: new FakeVnpayPaymentService(succeed: false), vnpayOptions: options)
            .PlaceAsync(Request(seats));

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingResultKind.Success, result.Kind);

        _db.ChangeTracker.Clear();
        var booking = await _db.Bookings.Include(b => b.Payment).SingleAsync();
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(PaymentStatus.Succeeded, booking.Payment!.Status);
        Assert.StartsWith("SIM-", booking.Payment.TransactionId);
        Assert.Single(await _db.OutboxEmails.ToListAsync());
    }

    [Fact]
    public async Task Failed_payment_on_return_frees_the_seats_for_other_customers()
    {
        var seats = await StandardSeatIdsAsync(2);

        var placed = await Service().PlaceAsync(Request(seats));
        Assert.True(placed.IsRedirectToPayment);

        // VNPAY trả về với mã lỗi (khách hủy / thẻ bị từ chối) — chữ ký vẫn hợp lệ.
        var confirm = await Service().ConfirmAsync(placed.BookingCode!, new VnpayVerifyResult(
            SignatureValid: true,
            PaymentSucceeded: false,
            TxnRef: placed.BookingCode,
            ResponseCode: "24",
            TransactionId: null,
            BankCode: null,
            Amount: 180_000m,
            FailureReason: "Khách hủy thanh toán."));

        Assert.Equal(BookingResultKind.PaymentFailed, confirm.Kind);

        _db.ChangeTracker.Clear();
        Assert.Equal(BookingStatus.Cancelled, (await _db.Bookings.SingleAsync()).Status);
        Assert.Empty(await _db.BookingSeats.ToListAsync());
        Assert.Empty(await _db.OutboxEmails.ToListAsync());
    }

    [Fact]
    public async Task Return_and_IPN_racing_on_the_same_order_confirm_it_exactly_once()
    {
        var seats = await StandardSeatIdsAsync(2);
        var code = (await Service().PlaceAsync(Request(seats))).BookingCode!;

        // Bên thứ hai (IPN) đã đọc đơn lúc còn Pending trước khi bên thứ nhất (Return) xác nhận xong.
        await using var other = new CinemaDbContext(new DbContextOptionsBuilder<CinemaDbContext>().UseSqlite(_connection).Options);
        await other.Bookings.Include(b => b.Payment).Include(b => b.Seats).SingleAsync(b => b.BookingCode == code);

        VnpayVerifyResult Paid() => new(true, true, code, "00", "VNPAY-TEST", "NCB", 180_000m, null);
        Assert.True((await Service().ConfirmAsync(code, Paid())).IsSuccess);

        var late = await new BookingService(other,
            new BookedSeatQuery(other, new FixedTimeProvider(Now), Microsoft.Extensions.Options.Options.Create(new VnpayOptions())),
            new SeatPricingService(), new FakeVnpayPaymentService(succeed: true), new BookingCodeGenerator(),
            new FixedTimeProvider(Now), Microsoft.Extensions.Options.Options.Create(new VnpayOptions()),
            new DisplayTimeZone(), NullLogger<BookingService>.Instance).ConfirmAsync(code, Paid());

        Assert.False(late.IsSuccess);
        _db.ChangeTracker.Clear();
        Assert.Equal(BookingStatus.Confirmed, (await _db.Bookings.SingleAsync()).Status);
        Assert.Single(await _db.OutboxEmails.ToListAsync());
    }

    [Fact]
    public async Task Invalid_signature_never_cancels_the_booking()
    {
        var seat = (await StandardSeatIdsAsync(1))[0];

        var placed = await Service().PlaceAsync(Request(new[] { seat }));
        Assert.True(placed.IsRedirectToPayment);

        // Ai đó gọi ReturnUrl với tham số rác: chữ ký sai thì tuyệt đối không được hủy đơn của khách.
        var confirm = await Service().ConfirmAsync(placed.BookingCode!, new VnpayVerifyResult(
            SignatureValid: false,
            PaymentSucceeded: false,
            TxnRef: placed.BookingCode,
            ResponseCode: "24",
            TransactionId: null,
            BankCode: null,
            Amount: 90_000m,
            FailureReason: "Chu ky khong hop le."));

        Assert.False(confirm.IsSuccess);

        _db.ChangeTracker.Clear();
        Assert.Equal(BookingStatus.Pending, (await _db.Bookings.SingleAsync()).Status);
        Assert.Single(await _db.BookingSeats.ToListAsync());
    }

    [Fact]
    public async Task Abandoned_pending_booking_releases_the_seat_after_the_payment_window()
    {
        var seat = (await StandardSeatIdsAsync(1))[0];

        // Đơn Pending tạo cách đây 40 phút (> 10 phút VNPAY + 2 phút gia hạn) và chưa từng được xác nhận.
        _db.Bookings.Add(new Booking
        {
            BookingCode = "CB-STALE1",
            ShowtimeId = _showtime.Id,
            CustomerName = "Khách bỏ dở",
            CustomerEmail = "bo@example.com",
            BookingTimeUtc = Now.AddMinutes(-40),
            TotalAmount = 90_000m,
            Status = BookingStatus.Pending,
            Seats = { new BookingSeat { SeatId = seat, ShowtimeId = _showtime.Id, PriceAtBooking = 90_000m } },
            Payment = new Payment { Amount = 90_000m, PaymentMethod = "VNPAY", Status = PaymentStatus.Pending, CreatedUtc = Now.AddMinutes(-40) },
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var bookedSeats = new BookedSeatQuery(_db, new FixedTimeProvider(Now),
            Microsoft.Extensions.Options.Options.Create(new CinemaBooking.Infrastructure.VnpayOptions()));
        Assert.DoesNotContain(seat, await bookedSeats.GetBookedSeatIdsAsync(_showtime.Id));

        // Người khác chọn đúng ghế đó: đơn bỏ dở bị hết hạn, ghế được trả và bán cho người mới.
        var result = await Service().PlaceAsync(Request(new[] { seat }));

        Assert.True(result.IsRedirectToPayment);

        _db.ChangeTracker.Clear();
        Assert.Equal(BookingStatus.Expired, (await _db.Bookings.SingleAsync(b => b.BookingCode == "CB-STALE1")).Status);
        Assert.Single(await _db.BookingSeats.ToListAsync());
    }

    [Fact]
    public async Task Automatic_promotion_is_applied_to_price_and_recorded_on_each_seat()
    {
        _db.Promotions.Add(new Promotion
        {
            TheaterId = _room.TheaterId, Title = "Suất sớm", IsActive = true,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31),
            DiscountPercent = 25, ApplicableSeatType = SeatType.Standard,
        });
        await _db.SaveChangesAsync();

        var seats = await StandardSeatIdsAsync(2);

        var placed = await Service().PlaceAsync(Request(seats));
        Assert.True(placed.IsRedirectToPayment);

        _db.ChangeTracker.Clear();
        var booking = await _db.Bookings.Include(b => b.Seats).Include(b => b.Payment)
            .SingleAsync(b => b.BookingCode == placed.BookingCode);

        // 90.000 giảm 25% = 67.500 mỗi ghế; tổng tiền đơn và số tiền thanh toán khớp giá đã giảm.
        Assert.Equal(135_000m, booking.TotalAmount);
        Assert.Equal(135_000m, booking.Payment!.Amount);
        Assert.All(booking.Seats, s =>
        {
            Assert.Equal(67_500m, s.PriceAtBooking);
            Assert.Equal(22_500m, s.DiscountAmount);
            Assert.Equal("Suất sớm", s.PromotionTitle);
        });
    }

    [Fact]
    public async Task Promotion_for_another_seat_type_or_outside_its_dates_does_not_change_the_price()
    {
        _db.Promotions.AddRange(
            new Promotion
            {
                TheaterId = _room.TheaterId, Title = "Chỉ VIP", IsActive = true,
                StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31),
                DiscountPercent = 50, ApplicableSeatType = SeatType.Vip,
            },
            new Promotion
            {
                TheaterId = _room.TheaterId, Title = "Hết hạn", IsActive = true,
                StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
                DiscountPercent = 50,
            });
        await _db.SaveChangesAsync();

        var seats = await StandardSeatIdsAsync(1);
        var placed = await Service().PlaceAsync(Request(seats));

        _db.ChangeTracker.Clear();
        var booking = await _db.Bookings.Include(b => b.Seats).SingleAsync(b => b.BookingCode == placed.BookingCode);
        Assert.Equal(90_000m, booking.TotalAmount);
        Assert.Equal(0m, booking.Seats.Single().DiscountAmount);
        Assert.Null(booking.Seats.Single().PromotionTitle);
    }

    [Fact]
    public async Task Expiry_only_claims_orders_still_Pending_and_never_overwrites_a_Confirmed_one()
    {
        var seats = await StandardSeatIdsAsync(2);
        var sold = await Service().PlaceAsync(Request(new[] { seats[0] }, "a@example.com"));
        var abandoned = await Service().PlaceAsync(Request(new[] { seats[1] }, "b@example.com"));
        await Service().ConfirmAsync(sold.BookingCode!, new VnpayVerifyResult(
            true, true, sold.BookingCode, "00", "TX", "NCB", 90_000m, null));

        // Cả hai đơn đều quá cửa sổ giữ; đơn đã Confirmed (IPN đến đúng lúc quét) phải giữ nguyên.
        await _db.Bookings.ExecuteUpdateAsync(s => s.SetProperty(b => b.BookingTimeUtc, Now.AddMinutes(-30)));
        _db.ChangeTracker.Clear();

        var freed = await PendingExpiry.ExpireAsync(_db, Now.AddMinutes(-12), null, 200, default);

        Assert.Equal(new[] { seats[1] }, freed.Single().SeatIds);
        Assert.Equal(BookingStatus.Confirmed, (await _db.Bookings.SingleAsync(b => b.BookingCode == sold.BookingCode)).Status);
        Assert.Equal(1, await _db.BookingSeats.CountAsync(s => s.SeatId == seats[0]));
        Assert.Equal(BookingStatus.Expired, (await _db.Bookings.SingleAsync(b => b.BookingCode == abandoned.BookingCode)).Status);
        Assert.Equal(0, await _db.BookingSeats.CountAsync(s => s.SeatId == seats[1]));
    }

    [Fact]
    public async Task With_holds_enabled_an_order_needs_the_callers_own_unexpired_hold_and_consumes_it()
    {
        var clock = new MutableTimeProvider(Now);
        var holds = Holds(clock);
        var seat = (await StandardSeatIdsAsync(1))[0];
        _db.Users.AddRange(new ApplicationUser { Id = "u1", UserName = "u1" }, new ApplicationUser { Id = "u2", UserName = "u2" });
        await _db.SaveChangesAsync();

        // Chưa giữ ghế: bị từ chối, không tạo đơn.
        var none = await Service(holds: holds).PlaceAsync(Request(new[] { seat }, userId: "u1"));
        Assert.Equal(BookingResultKind.SeatsUnavailable, none.Kind);
        Assert.Equal(0, await _db.Bookings.CountAsync());

        // Ghế do người khác giữ: người này không đặt được.
        Assert.True((await holds.SelectAsync("u2", _showtime.Id, seat, 8)).Ok);
        var foreign = await Service(holds: holds).PlaceAsync(Request(new[] { seat }, userId: "u1"));
        Assert.Contains("hết hạn", foreign.Message);
        Assert.Equal(0, await _db.Bookings.CountAsync());

        // Chủ ghế đặt được, và lượt giữ được tiêu thụ (không còn dòng hold/session).
        var own = await Service(holds: holds).PlaceAsync(Request(new[] { seat }, userId: "u2"));
        Assert.True(own.IsRedirectToPayment);
        Assert.Equal(0, await _db.SeatHolds.CountAsync());
        Assert.Equal(0, await _db.SeatHoldSessions.CountAsync());
    }

    [Fact]
    public async Task With_holds_enabled_an_expired_hold_is_refused_and_no_order_is_created()
    {
        var clock = new MutableTimeProvider(Now);
        var holds = Holds(clock);
        var seat = (await StandardSeatIdsAsync(1))[0];
        Assert.True((await holds.SelectAsync("u1", _showtime.Id, seat, 8)).Ok);
        clock.Advance(TimeSpan.FromMinutes(10));

        var result = await Service(holds: holds).PlaceAsync(Request(new[] { seat }, userId: "u1"));

        Assert.Equal(BookingResultKind.SeatsUnavailable, result.Kind);
        Assert.Contains("hết hạn", result.Message);
        Assert.Equal(0, await _db.Bookings.CountAsync());
        Assert.Equal(0, await _db.BookingSeats.CountAsync());
    }

    [Fact]
    public async Task With_holds_enabled_a_guest_without_an_account_cannot_order()
    {
        var seat = (await StandardSeatIdsAsync(1))[0];

        var result = await Service(holds: Holds(new MutableTimeProvider(Now))).PlaceAsync(Request(new[] { seat }));

        Assert.Equal(BookingResultKind.SeatsUnavailable, result.Kind);
        Assert.Contains("đăng nhập", result.Message);
    }

    [Fact]
    public async Task With_holds_enabled_a_signed_in_user_is_not_blocked_by_someone_elses_pending_order_with_the_same_email()
    {
        var clock = new MutableTimeProvider(Now);
        var holds = Holds(clock);
        var seats = await StandardSeatIdsAsync(2);
        _db.Users.AddRange(new ApplicationUser { Id = "u1", UserName = "u1" }, new ApplicationUser { Id = "u2", UserName = "u2" });
        await _db.SaveChangesAsync();
        Assert.True((await holds.SelectAsync("u1", _showtime.Id, seats[0], 8)).Ok);
        Assert.True((await holds.SelectAsync("u2", _showtime.Id, seats[1], 8)).Ok);

        Assert.True((await Service(holds: holds).PlaceAsync(Request(new[] { seats[0] }, "shared@example.com", "u1"))).IsRedirectToPayment);
        var other = await Service(holds: holds).PlaceAsync(Request(new[] { seats[1] }, "shared@example.com", "u2"));

        Assert.True(other.IsRedirectToPayment);
    }

    private BookingService Service(
        IBookedSeatQuery? bookedSeats = null,
        IVnpayPaymentService? vnpay = null,
        VnpayOptions? vnpayOptions = null,
        SeatHoldService? holds = null) =>
        new(_db,
            bookedSeats ?? new BookedSeatQuery(_db, new FixedTimeProvider(Now),
                Microsoft.Extensions.Options.Options.Create(new VnpayOptions())),
            new SeatPricingService(),
            vnpay ?? new FakeVnpayPaymentService(succeed: true),
            new BookingCodeGenerator(),
            new FixedTimeProvider(Now),
            Microsoft.Extensions.Options.Options.Create(vnpayOptions ?? new VnpayOptions()),
            new DisplayTimeZone(),
            NullLogger<BookingService>.Instance,
            holds: holds);

    private SeatHoldService Holds(MutableTimeProvider clock) =>
        new(_db, clock, Microsoft.Extensions.Options.Options.Create(new SeatHoldOptions { DurationMinutes = 10 }));

    private BookingRequest Request(IReadOnlyList<int> seatIds, string email = "a@example.com", string? userId = null) =>
        new(_showtime.Id, seatIds, "Nguyễn Văn A", email, "0900000000", userId);

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

    private sealed class FakeVnpayPaymentService : IVnpayPaymentService
    {
        private readonly bool _succeed;
        public FakeVnpayPaymentService(bool succeed) => _succeed = succeed;

        public Task<VnpayPaymentResult> CreateVnpayPaymentAsync(VnpayPaymentRequest request, CancellationToken ct = default) =>
            Task.FromResult(_succeed
                ? new VnpayPaymentResult(true, $"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef={request.TxnRef}", null, request.TxnRef, "00", null, null, null)
                : new VnpayPaymentResult(false, null, null, request.TxnRef, null, null, null, "Gia cong khong thanh cong."));

        public Task<VnpayVerifyResult> VerifyVnpayPaymentAsync(VnpayPaymentRequest request, IDictionary<string, string?> response, CancellationToken ct = default) =>
            Task.FromResult(new VnpayVerifyResult(
                SignatureValid: true,
                PaymentSucceeded: true,
                TxnRef: response["vnp_TxnRef"],
                ResponseCode: response["vnp_ResponseCode"],
                TransactionId: response["vnp_TransactionNo"],
                BankCode: response["vnp_BankCode"],
                Amount: 180_000m,
                FailureReason: null));
    }

    private sealed class CountingVnpay : IVnpayPaymentService
    {
        public int Calls { get; private set; }

        public Task<VnpayPaymentResult> CreateVnpayPaymentAsync(VnpayPaymentRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new VnpayPaymentResult(true, "https://sandbox.vnpayment.vn/x", null, request.TxnRef, "00", null, null, null));
        }

        public Task<VnpayVerifyResult> VerifyVnpayPaymentAsync(VnpayPaymentRequest request, IDictionary<string, string?> response, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoBookedSeats : IBookedSeatQuery
    {
        public Task<IReadOnlySet<int>> GetBookedSeatIdsAsync(int showtimeId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
    }
}
