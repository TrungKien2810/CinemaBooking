using CinemaBooking.Domain;
using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web;
using CinemaBooking.Web.Areas.Admin.Controllers;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Controllers;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

public class PricingRulesTests
{
    // Thứ Hai 02/11/2026 và Thứ Bảy 07/11/2026 — chỉ để chọn loại ngày.
    private static readonly DateTime Monday = new(2026, 11, 2);
    private static readonly DateTime Friday = new(2026, 11, 6);
    private static readonly DateTime Saturday = new(2026, 11, 7);

    private static TheaterPricing Policy() => new()
    {
        TheaterId = 1, StandardPrice = 80_000m, ThreeDPrice = 100_000m, ImaxPrice = 130_000m, FourDxPrice = 150_000m,
        VipSurcharge = 20_000m, CoupleSurcharge = 10_000m, PeakSurcharge = 25_000m, PeakFromHour = 18, WeekendSurcharge = 10_000m,
    };

    [Theory]
    [InlineData(RoomType.Standard, 80_000)]
    [InlineData(RoomType.ThreeD, 100_000)]
    [InlineData(RoomType.Imax, 130_000)]
    [InlineData(RoomType.FourDx, 150_000)]
    public void Price_follows_the_room_type_on_a_weekday_morning(RoomType room, int expected)
    {
        Assert.Equal(expected, PricingRules.BasePrice(Policy(), room, Monday.AddHours(10)));
    }

    [Fact]
    public void Peak_hours_and_weekends_add_their_surcharges_and_stack()
    {
        var p = Policy();

        Assert.Equal(105_000m, PricingRules.BasePrice(p, RoomType.Standard, Monday.AddHours(18)));       // + cao điểm
        Assert.Equal(90_000m, PricingRules.BasePrice(p, RoomType.Standard, Saturday.AddHours(10)));      // + cuối tuần
        Assert.Equal(115_000m, PricingRules.BasePrice(p, RoomType.Standard, Saturday.AddHours(19)));     // cả hai
        Assert.Equal(80_000m, PricingRules.BasePrice(p, RoomType.Standard, Monday.AddHours(17).AddMinutes(59)));
    }

    [Fact]
    public void Friday_counts_as_weekend_only_from_peak_hour()
    {
        var p = Policy();

        Assert.False(PricingRules.IsWeekend(p, Friday.AddHours(12)));
        Assert.True(PricingRules.IsWeekend(p, Friday.AddHours(18)));
        Assert.True(PricingRules.IsWeekend(p, Saturday.AddHours(8)));
        Assert.False(PricingRules.IsWeekend(p, Monday.AddHours(20)));
    }

    [Fact]
    public void Seat_price_is_base_plus_a_surcharge_for_vip_and_each_half_of_a_couple_seat()
    {
        var p = Policy();
        var start = Monday.AddHours(10);

        Assert.Equal(80_000m, PricingRules.SeatPrice(p, RoomType.Standard, SeatType.Standard, start));
        Assert.Equal(100_000m, PricingRules.SeatPrice(p, RoomType.Standard, SeatType.Vip, start));
        Assert.Equal(90_000m, PricingRules.SeatPrice(p, RoomType.Standard, SeatType.Couple, start));
        // Phòng đắt hơn kéo cả ghế VIP lên theo: giá phụ thuộc phòng + ghế, KHÔNG phụ thuộc phim.
        Assert.Equal(150_000m, PricingRules.SeatPrice(p, RoomType.Imax, SeatType.Vip, start));
    }

    [Fact]
    public void Seat_pricing_service_uses_surcharges_when_given_and_falls_back_to_the_old_multiplier()
    {
        var service = new SeatPricingService();

        Assert.Equal(110_000m, service.PriceFor(90_000m, SeatType.Vip, new SeatSurcharges(20_000m, 10_000m)));
        Assert.Equal(100_000m, service.PriceFor(90_000m, SeatType.Couple, new SeatSurcharges(20_000m, 10_000m)));
        Assert.Equal(90_000m, service.PriceFor(90_000m, SeatType.Standard, new SeatSurcharges(20_000m, 10_000m)));
        Assert.Equal(108_000m, service.PriceFor(90_000m, SeatType.Vip, null));
    }

    [Fact]
    public void The_public_price_matrix_matches_the_real_pricing_formula_row_by_row()
    {
        var p = Policy();

        var table = PriceMatrixModel.Build(p, [RoomType.ThreeD]).Tables.Single();

        Assert.Equal(RoomType.ThreeD, table.Type);
        Assert.Equal(4, table.Rows.Count);
        var weekdayDay = table.Rows[0];
        var weekendPeak = table.Rows[3];
        Assert.Equal(PricingRules.SeatPrice(p, RoomType.ThreeD, SeatType.Standard, Monday.AddHours(17)), weekdayDay.Standard);
        Assert.Equal(100_000m, weekdayDay.Standard);
        Assert.Equal(120_000m, weekdayDay.Vip);
        Assert.Equal(110_000m, weekdayDay.CoupleSeat);
        Assert.Equal(220_000m, weekdayDay.CouplePair);   // ghế đôi bán theo cặp = 2 ghế
        Assert.Equal(PricingRules.SeatPrice(p, RoomType.ThreeD, SeatType.Vip, Saturday.AddHours(18)), weekendPeak.Vip);
    }

    [Fact]
    public void Default_pricing_gives_dearer_rooms_higher_prices()
    {
        var p = PricingRules.DefaultFor(5, 90_000m);

        Assert.Equal(5, p.TheaterId);
        Assert.True(p.StandardPrice < p.ThreeDPrice && p.ThreeDPrice < p.ImaxPrice && p.ImaxPrice < p.FourDxPrice);
        Assert.Equal(18, p.PeakFromHour);
    }
}

public class VoucherRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);

    private static Voucher Percent(int pct = 10, decimal? cap = 30_000m, decimal? min = 100_000m) => new()
    {
        Code = "CINE10", Title = "t", DiscountPercent = pct, MaxDiscountAmount = cap, MinOrderAmount = min,
        StartDate = Today.AddDays(-5), EndDate = Today.AddDays(5), PerCustomerLimit = 1,
    };

    private static VoucherResult Eval(Voucher? v, decimal subtotal = 200_000m, int theater = 1, int total = 0, int mine = 0, DateOnly? today = null) =>
        VoucherRules.Evaluate(v, today ?? Today, theater, subtotal, total, mine);

    [Fact]
    public void Percent_discount_is_capped()
    {
        var r = Eval(Percent(), subtotal: 500_000m);

        Assert.True(r.IsOk);
        Assert.Equal(30_000m, r.Discount);   // 10% = 50.000 nhưng trần 30.000
    }

    [Fact]
    public void Fixed_amount_and_percent_without_cap_work()
    {
        var fixedV = new Voucher { Code = "A", Title = "t", DiscountAmount = 20_000m, StartDate = Today, EndDate = Today, PerCustomerLimit = 0 };

        Assert.Equal(20_000m, Eval(fixedV).Discount);
        Assert.Equal(20_000m, Eval(Percent(10, null, null), 200_000m).Discount);
    }

    [Fact]
    public void A_discount_never_takes_the_order_below_the_minimum_payable_amount()
    {
        var fullOff = Percent(100, null, null);
        var huge = new Voucher { Code = "H", Title = "t", DiscountAmount = 9_000_000m, StartDate = Today, EndDate = Today, PerCustomerLimit = 0 };

        Assert.Equal(199_000m, Eval(fullOff, 200_000m).Discount);
        Assert.Equal(49_000m, Eval(huge, 50_000m).Discount);
        Assert.Equal(200_000m - VoucherRules.MinPayable, VoucherRules.ComputeDiscount(huge, 200_000m));
    }

    [Theory]
    [InlineData(-6, VoucherCheck.Ok)]      // đã bắt đầu từ lâu → ok (kiểm ngày bên dưới)
    public void Dates_are_inclusive_at_both_ends(int _, VoucherCheck __)
    {
        var v = Percent();
        Assert.True(Eval(v, today: v.StartDate).IsOk);
        Assert.True(Eval(v, today: v.EndDate).IsOk);
        Assert.Equal(VoucherCheck.NotStarted, Eval(v, today: v.StartDate.AddDays(-1)).Check);
        Assert.Equal(VoucherCheck.Expired, Eval(v, today: v.EndDate.AddDays(1)).Check);
    }

    [Fact]
    public void Each_rule_reports_its_own_reason_in_vietnamese()
    {
        var inactive = Percent(); inactive.IsActive = false;
        var branch = Percent(); branch.TheaterId = 7;
        var limited = Percent(); limited.UsageLimit = 5;

        Assert.Equal(VoucherCheck.NotFound, Eval(null).Check);
        Assert.Equal(VoucherCheck.Inactive, Eval(inactive).Check);
        Assert.Equal(VoucherCheck.WrongBranch, Eval(branch, theater: 1).Check);
        Assert.True(Eval(branch, theater: 7).IsOk);
        Assert.Equal(VoucherCheck.BelowMinimum, Eval(Percent(), subtotal: 99_999m).Check);
        Assert.Equal(VoucherCheck.UsageLimitReached, Eval(limited, total: 5).Check);
        Assert.True(Eval(limited, total: 4).IsOk);
        Assert.Equal(VoucherCheck.CustomerLimitReached, Eval(Percent(), mine: 1).Check);
        Assert.Contains("100.000", Eval(Percent(), subtotal: 50_000m).Message);
        Assert.All(new[] { VoucherCheck.NotFound, VoucherCheck.Expired }, c => Assert.False(string.IsNullOrWhiteSpace(
            c == VoucherCheck.NotFound ? Eval(null).Message : Eval(Percent(), today: Today.AddDays(30)).Message)));
    }

    [Fact]
    public void Customer_limit_zero_means_unlimited()
    {
        var v = Percent(); v.PerCustomerLimit = 0;

        Assert.True(Eval(v, mine: 99).IsOk);
    }

    [Theory]
    [InlineData(18_000, new[] { 90_000, 90_000 }, new[] { 9_000, 9_000 })]
    [InlineData(10_000, new[] { 100_000, 100_000, 100_000 }, new[] { 3_333, 3_333, 3_334 })]
    [InlineData(5_000, new[] { 30_000 }, new[] { 5_000 })]
    public void Allocation_sums_exactly_to_the_discount(int discount, int[] prices, int[] expected)
    {
        var shares = VoucherRules.Allocate(discount, prices.Select(p => (decimal)p).ToList());

        Assert.Equal(expected.Select(e => (decimal)e), shares);
        Assert.Equal(discount, shares.Sum());
    }

    [Fact]
    public void Allocation_is_proportional_and_never_exceeds_a_seat_price()
    {
        var shares = VoucherRules.Allocate(50_000m, [200_000m, 10_000m]);

        Assert.Equal(50_000m, shares.Sum());
        Assert.True(shares[1] <= 10_000m);
        Assert.True(shares[0] > shares[1]);
    }

    [Theory]
    [InlineData("  cine10 ", "CINE10")]
    [InlineData(null, "")]
    public void Codes_are_trimmed_and_upper_cased(string? input, string expected) =>
        Assert.Equal(expected, VoucherRules.Normalize(input));
}

public class VoucherBookingFlowTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection = new("Filename=:memory:");
    private readonly CinemaBooking.Infrastructure.Services.InMemorySeatLockManager _locks = new();
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
        _db.AddRange(theater, _room, movie);
        await _db.SaveChangesAsync();
        _db.Seats.AddRange(SeatGenerator.Generate(_room, SeatLayoutPreset.AllStandard, 0));
        _showtime = new Showtime { Movie = movie, Room = _room, StartTimeUtc = Now.AddHours(2), EndTimeUtc = Now.AddHours(4), BasePrice = 90_000m };
        _db.Showtimes.Add(_showtime);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private Voucher AddVoucher(string code, Action<Voucher>? tweak = null)
    {
        var v = new Voucher
        {
            Code = code, Title = "Giảm giá", DiscountPercent = 10, MaxDiscountAmount = 30_000m, MinOrderAmount = 100_000m,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31), PerCustomerLimit = 1,
        };
        tweak?.Invoke(v);
        _db.Vouchers.Add(v);
        _db.SaveChanges();
        return v;
    }

    private BookingService Service() => new(
        _db,
        new CinemaBooking.Web.Services.BookedSeatQuery(_db, new FixedTimeProvider(Now), Options.Create(new CinemaBooking.Infrastructure.VnpayOptions())),
        _locks, new SeatPricingService(), new PaymentStub(), new CinemaBooking.Infrastructure.Services.BookingCodeGenerator(),
        new FixedTimeProvider(Now), Options.Create(new CinemaBooking.Infrastructure.VnpayOptions { SimulateWhenUnconfigured = false }), new DisplayTimeZone(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<BookingService>.Instance,
        new VoucherService(_db, new FixedTimeProvider(Now), Options.Create(new DisplayTimeZone())));

    private async Task<BookingResult> PlaceAsync(int seatCount, string? code, string email = "a@example.com", int firstColumn = 1)
    {
        var ids = await _db.Seats.Where(s => s.RoomId == _room.Id && s.RowNumber == 1 && s.ColumnNumber >= firstColumn)
            .OrderBy(s => s.ColumnNumber).Select(s => s.Id).Take(seatCount).ToListAsync();
        foreach (var id in ids) _locks.TryLock(new SeatKey(_showtime.Id, id), "conn", null, TimeSpan.FromMinutes(5));
        return await Service().PlaceAsync(new BookingRequest(_showtime.Id, ids, "conn", "Khách", email, "0900000000", null, code));
    }

    [Fact]
    public async Task A_valid_code_is_split_across_the_seats_so_revenue_still_adds_up()
    {
        AddVoucher("CINE10");

        var result = await PlaceAsync(2, "cine10");   // gõ thường vẫn nhận

        Assert.True(result.IsRedirectToPayment);
        _db.ChangeTracker.Clear();
        var booking = await _db.Bookings.Include(b => b.Seats).Include(b => b.Payment).SingleAsync();
        Assert.Equal("CINE10", booking.VoucherCode);
        Assert.Equal(18_000m, booking.VoucherDiscount);      // 10% của 180.000
        Assert.Equal(162_000m, booking.TotalAmount);
        Assert.Equal(162_000m, booking.Payment!.Amount);     // cổng thanh toán nhận số tiền SAU giảm
        Assert.Equal(booking.TotalAmount, booking.Seats.Sum(s => s.PriceAtBooking));   // doanh thu theo ghế khớp tổng đơn
        Assert.All(booking.Seats, s => Assert.Equal(9_000m, s.VoucherShare));
    }

    [Fact]
    public async Task An_expired_or_unknown_code_refuses_the_order_and_writes_nothing()
    {
        AddVoucher("OLD", v => { v.StartDate = new DateOnly(2026, 8, 1); v.EndDate = new DateOnly(2026, 8, 31); });

        var expired = await PlaceAsync(2, "OLD");
        var unknown = await PlaceAsync(2, "NOPE");

        Assert.Equal(BookingResultKind.VoucherInvalid, expired.Kind);
        Assert.Contains("hết hạn", expired.Message);
        Assert.Equal(BookingResultKind.VoucherInvalid, unknown.Kind);
        Assert.Empty(await _db.Bookings.ToListAsync());
    }

    [Fact]
    public async Task The_minimum_order_is_checked_against_the_real_server_side_total()
    {
        AddVoucher("BIG", v => v.MinOrderAmount = 300_000m);

        var result = await PlaceAsync(2, "BIG");   // chỉ 180.000

        Assert.Equal(BookingResultKind.VoucherInvalid, result.Kind);
        Assert.Contains("300.000", result.Message);
    }

    [Fact]
    public async Task One_use_per_customer_is_enforced_by_email_and_cancelled_orders_give_the_use_back()
    {
        AddVoucher("ONCE");
        Assert.True((await PlaceAsync(2, "ONCE", "same@example.com")).IsRedirectToPayment);

        var again = await PlaceAsync(2, "ONCE", "SAME@example.com", firstColumn: 3);   // khác hoa/thường vẫn là cùng khách
        Assert.Equal(BookingResultKind.VoucherInvalid, again.Kind);
        Assert.Contains("đã dùng", again.Message);

        var other = await PlaceAsync(2, "ONCE", "other@example.com", firstColumn: 3);
        Assert.True(other.IsRedirectToPayment);

        // Đơn đầu bị hủy → khách đó dùng lại được.
        var first = await _db.Bookings.OrderBy(b => b.Id).FirstAsync();
        first.Status = BookingStatus.Cancelled;
        await _db.SaveChangesAsync();
        _db.BookingSeats.RemoveRange(await _db.BookingSeats.Where(s => s.BookingId == first.Id).ToListAsync());
        await _db.SaveChangesAsync();
        Assert.True((await PlaceAsync(2, "ONCE", "same@example.com", firstColumn: 1)).IsRedirectToPayment);
    }

    [Fact]
    public async Task The_total_usage_limit_stops_the_code_once_it_is_reached()
    {
        AddVoucher("TWO", v => { v.UsageLimit = 2; v.PerCustomerLimit = 0; v.MinOrderAmount = null; });

        Assert.True((await PlaceAsync(1, "TWO", "a@x.vn", 1)).IsRedirectToPayment);
        Assert.True((await PlaceAsync(1, "TWO", "b@x.vn", 2)).IsRedirectToPayment);

        var third = await PlaceAsync(1, "TWO", "c@x.vn", 3);

        Assert.Equal(BookingResultKind.VoucherInvalid, third.Kind);
        Assert.Contains("hết lượt", third.Message);
    }

    [Fact]
    public async Task A_branch_specific_code_is_refused_at_another_branch()
    {
        var other = new Theater { Name = "Khác", City = "Hà Nội" };
        _db.Theaters.Add(other);
        await _db.SaveChangesAsync();
        AddVoucher("ELSEWHERE", v => v.TheaterId = other.Id);

        var result = await PlaceAsync(2, "ELSEWHERE");

        Assert.Equal(BookingResultKind.VoucherInvalid, result.Kind);
        Assert.Contains("chi nhánh", result.Message);
    }

    [Fact]
    public async Task Booking_without_a_code_is_unchanged()
    {
        AddVoucher("UNUSED");

        var result = await PlaceAsync(2, null);

        Assert.True(result.IsRedirectToPayment);
        _db.ChangeTracker.Clear();
        var booking = await _db.Bookings.Include(b => b.Seats).SingleAsync();
        Assert.Null(booking.VoucherCode);
        Assert.Equal(0m, booking.VoucherDiscount);
        Assert.Equal(180_000m, booking.TotalAmount);
    }

    private sealed class PaymentStub : CinemaBooking.Domain.Abstractions.IVnpayPaymentService
    {
        public Task<CinemaBooking.Domain.Abstractions.VnpayPaymentResult> CreateVnpayPaymentAsync(
            CinemaBooking.Domain.Abstractions.VnpayPaymentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CinemaBooking.Domain.Abstractions.VnpayPaymentResult(
                true, "https://pay.test/x?vnp_TxnRef=" + request.TxnRef, null, request.TxnRef, "00", null, null, null));

        public Task<CinemaBooking.Domain.Abstractions.VnpayVerifyResult> VerifyVnpayPaymentAsync(
            CinemaBooking.Domain.Abstractions.VnpayPaymentRequest request, IDictionary<string, string?> response, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}

public class PricesPageAndAdminTests
{
    [Fact]
    public async Task Public_prices_page_shows_only_the_room_types_the_branch_really_has_and_compares_branches()
    {
        var db = TestDatabase.Create();
        var a = new Theater { Name = "CineSmart A", City = "Hà Nội" };
        var b = new Theater { Name = "CineSmart B", City = "Đà Nẵng" };
        db.Theaters.AddRange(a, b);
        await db.SaveChangesAsync();
        db.Rooms.AddRange(
            new Room { TheaterId = a.Id, RoomName = "1", TotalRows = 2, TotalColumns = 4, RoomType = RoomType.Standard },
            new Room { TheaterId = a.Id, RoomName = "2", TotalRows = 2, TotalColumns = 4, RoomType = RoomType.Imax },
            new Room { TheaterId = b.Id, RoomName = "1", TotalRows = 2, TotalColumns = 4, RoomType = RoomType.Standard });
        db.TheaterPricings.Add(PricingRules.DefaultFor(a.Id, 90_000m));
        db.TheaterPricings.Add(PricingRules.DefaultFor(b.Id, 70_000m));
        await db.SaveChangesAsync();
        var controller = new PricesController(db, Options.Create(new DisplayTimeZone()), new FixedTimeProvider(new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc)));

        var model = Assert.IsType<PricesViewModel>(Assert.IsType<ViewResult>(await controller.Index(a.Id, default)).Model);

        Assert.Equal(a.Id, model.Selected!.Id);
        Assert.Equal([RoomType.Standard, RoomType.Imax], model.Matrix!.Tables.Select(t => t.Type));
        Assert.Equal(2, model.Compare.Count);
        Assert.True(model.Compare.Single(c => c.Id == a.Id).IsSelected);
        Assert.True(model.Compare.Single(c => c.Id == a.Id).WeekdayDay > model.Compare.Single(c => c.Id == b.Id).WeekdayDay);
    }

    [Fact]
    public async Task Unknown_or_closed_branch_ids_fall_back_and_a_branch_without_pricing_says_so()
    {
        var db = TestDatabase.Create();
        var open = new Theater { Name = "Mở", City = "Hà Nội" };
        var closed = new Theater { Name = "Đóng", City = "Hà Nội", IsActive = false };
        db.Theaters.AddRange(open, closed);
        await db.SaveChangesAsync();
        var controller = new PricesController(db, Options.Create(new DisplayTimeZone()), new FixedTimeProvider(DateTime.UtcNow));

        var model = Assert.IsType<PricesViewModel>(Assert.IsType<ViewResult>(await controller.Index(closed.Id, default)).Model);

        Assert.Equal(open.Id, model.Selected!.Id);
        Assert.False(model.HasPricing);
        Assert.Single(model.Branches);
    }

    [Fact]
    public async Task Branch_admin_cannot_read_or_change_another_branchs_pricing()
    {
        var db = TestDatabase.Create();
        var own = new Theater { Name = "Own", City = "Hà Nội" };
        var other = new Theater { Name = "Other", City = "Hà Nội" };
        db.Theaters.AddRange(own, other);
        await db.SaveChangesAsync();
        var controller = new PricingController(db, FakeBranchScope.Branch(own.Id), Options.Create(new DisplayTimeZone()), new FixedTimeProvider(DateTime.UtcNow));

        Assert.IsType<ViewResult>(await controller.Index(own.Id, default));
        Assert.IsType<ForbidResult>(await controller.Index(other.Id, default));
        Assert.IsType<ForbidResult>(await controller.Save(new PricingFormModel { TheaterId = other.Id }, default));
        Assert.IsType<ForbidResult>(await controller.ApplyToShowtimes(other.Id, default));
        Assert.Empty(await db.TheaterPricings.ToListAsync());
    }

    [Fact]
    public async Task Saving_creates_the_pricing_and_applying_it_reprices_only_future_showtimes_without_tickets()
    {
        var db = TestDatabase.Create();
        var theater = new Theater { Name = "T", City = "Hà Nội" };
        var room = new Room { Theater = theater, RoomName = "1", TotalRows = 2, TotalColumns = 4, RoomType = RoomType.Imax };
        var movie = db.SeedMovie("Phim");
        db.AddRange(theater, room);
        await db.SaveChangesAsync();
        var now = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);

        Showtime Add(DateTime startUtc, decimal price) =>
            db.Showtimes.Add(new Showtime { Movie = movie, Room = room, StartTimeUtc = startUtc, EndTimeUtc = startUtc.AddHours(2), BasePrice = price }).Entity;
        var future = Add(now.AddDays(2).AddHours(3), 50_000m);        // 10:00 giờ rạp, thứ Bảy 17/10? (ngày thường/cuối tuần tính bên dưới)
        var sold = Add(now.AddDays(3).AddHours(3), 50_000m);
        var past = Add(now.AddDays(-2), 50_000m);
        await db.SaveChangesAsync();
        db.Bookings.Add(new Booking { BookingCode = "CB-X", ShowtimeId = sold.Id, CustomerName = "a", CustomerEmail = "a@b.c", Status = BookingStatus.Confirmed });
        await db.SaveChangesAsync();

        var controller = new PricingController(db, FakeBranchScope.Admin(), Options.Create(new DisplayTimeZone()), new FixedTimeProvider(now));
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            new Microsoft.AspNetCore.Http.DefaultHttpContext(), new FakeTempDataProvider());

        await controller.Save(new PricingFormModel
        {
            TheaterId = theater.Id, StandardPrice = 80, ThreeDPrice = 100, ImaxPrice = 130, FourDxPrice = 150,
            VipSurcharge = 20, CoupleSurcharge = 10, PeakSurcharge = 20, WeekendSurcharge = 10, PeakFromHour = 17,
        }, default);

        var saved = await db.TheaterPricings.SingleAsync();
        Assert.Equal(130_000m, saved.ImaxPrice);             // gõ 130 = 130.000đ
        Assert.Equal(20_000m, saved.VipSurcharge);

        await controller.ApplyToShowtimes(theater.Id, default);

        db.ChangeTracker.Clear();
        var expected = PricingRules.BasePrice(saved, RoomType.Imax, new DateTimeOffset(future.StartTimeUtc).UtcDateTime.AddHours(7));
        Assert.Equal(expected, (await db.Showtimes.FindAsync(future.Id))!.BasePrice);
        Assert.Equal(50_000m, (await db.Showtimes.FindAsync(sold.Id))!.BasePrice);   // đã có vé → giữ nguyên
        Assert.Equal(50_000m, (await db.Showtimes.FindAsync(past.Id))!.BasePrice);   // đã chiếu → giữ nguyên
    }

    [Fact]
    public void Pricing_form_expands_thousands_and_rejects_nonsense()
    {
        var form = new PricingFormModel { StandardPrice = 95, VipSurcharge = 20, PeakFromHour = 18 };

        Assert.Equal(95_000m, form.StandardPrice);
        Assert.Equal(20_000m, form.VipSurcharge);
        Assert.Equal(0m, new PricingFormModel { PeakSurcharge = 0 }.PeakSurcharge);
    }
}

public class VouchersAdminTests
{
    private static async Task<(CinemaDbContext Db, Theater Own, Theater Other)> SeedAsync()
    {
        var db = TestDatabase.Create();
        var own = new Theater { Name = "Own", City = "Hà Nội" };
        var other = new Theater { Name = "Other", City = "Hà Nội" };
        db.Theaters.AddRange(own, other);
        await db.SaveChangesAsync();
        return (db, own, other);
    }

    private static VouchersController Controller(CinemaDbContext db, IBranchScope scope) =>
        new(db, scope, Options.Create(new DisplayTimeZone()), new FixedTimeProvider(new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc)))
        {
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                new Microsoft.AspNetCore.Http.DefaultHttpContext(), new FakeTempDataProvider()),
        };

    private static VoucherFormModel Form(int? theaterId, string code = "NEW10") => new()
    {
        Code = code, Title = "Mới", TheaterId = theaterId, DiscountKind = "percent", DiscountPercent = 10,
        StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 12, 31), PerCustomerLimit = 1,
    };

    [Fact]
    public async Task Branch_admin_codes_are_forced_to_their_own_branch_even_if_the_form_says_otherwise()
    {
        var (db, own, other) = await SeedAsync();
        var controller = Controller(db, FakeBranchScope.Branch(own.Id));

        await controller.Create(Form(other.Id, "FORGED"), default);   // form khai chi nhánh khác
        await controller.Create(Form(null, "GLOBAL"), default);       // form khai "mọi chi nhánh"

        var vouchers = await db.Vouchers.ToListAsync();
        Assert.All(vouchers, v => Assert.Equal(own.Id, v.TheaterId));
        Assert.DoesNotContain(vouchers, v => v.Code == "GLOBAL" && v.TheaterId != own.Id);
    }

    [Fact]
    public async Task Branch_admin_only_lists_and_edits_their_own_branchs_codes()
    {
        var (db, own, other) = await SeedAsync();
        db.Vouchers.AddRange(
            new Voucher { Code = "MINE", Title = "t", TheaterId = own.Id, DiscountPercent = 5, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) },
            new Voucher { Code = "THEIRS", Title = "t", TheaterId = other.Id, DiscountPercent = 5, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) },
            new Voucher { Code = "GLOBAL", Title = "t", DiscountPercent = 5, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) });
        await db.SaveChangesAsync();
        var controller = Controller(db, FakeBranchScope.Branch(own.Id));

        var model = Assert.IsType<VouchersIndexViewModel>(Assert.IsType<ViewResult>(await controller.Index(null, null, null, default)).Model);

        Assert.Equal(["MINE"], model.Items.Select(i => i.Code));
        var theirs = await db.Vouchers.SingleAsync(v => v.Code == "THEIRS");
        var global = await db.Vouchers.SingleAsync(v => v.Code == "GLOBAL");
        Assert.IsType<ForbidResult>(await controller.Edit(theirs.Id, default));
        Assert.IsType<ForbidResult>(await controller.Edit(global.Id, default));
        Assert.IsType<ForbidResult>(await controller.Delete(global.Id, default));
        Assert.True(await db.Vouchers.AnyAsync(v => v.Code == "GLOBAL"));
    }

    [Fact]
    public async Task Admin_can_create_a_global_code_but_duplicate_codes_are_rejected_case_insensitively()
    {
        var (db, _, _) = await SeedAsync();
        var controller = Controller(db, FakeBranchScope.Admin());

        await controller.Create(Form(null, "summer10"), default);
        var duplicate = await controller.Create(Form(null, "SUMMER10"), default);

        Assert.Equal("SUMMER10", (await db.Vouchers.SingleAsync()).Code);
        Assert.IsType<ViewResult>(duplicate);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task A_code_that_was_used_is_only_deactivated_and_an_unused_one_is_deleted()
    {
        var (db, _, _) = await SeedAsync();
        var movie = db.SeedMovie("Phim");
        var room = db.SeedRoom();
        await db.SaveChangesAsync();
        var showtime = db.SeedShowtime(movie, room, DateTime.UtcNow.AddDays(1));
        db.Vouchers.AddRange(
            new Voucher { Code = "USED", Title = "t", DiscountPercent = 5, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) },
            new Voucher { Code = "FRESH", Title = "t", DiscountPercent = 5, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) });
        await db.SaveChangesAsync();
        db.Bookings.Add(new Booking { BookingCode = "CB-V", ShowtimeId = showtime.Id, CustomerName = "a", CustomerEmail = "a@b.c", VoucherCode = "USED", Status = BookingStatus.Confirmed });
        await db.SaveChangesAsync();
        var controller = Controller(db, FakeBranchScope.Admin());

        await controller.Delete((await db.Vouchers.SingleAsync(v => v.Code == "USED")).Id, default);
        await controller.Delete((await db.Vouchers.SingleAsync(v => v.Code == "FRESH")).Id, default);

        Assert.False((await db.Vouchers.SingleAsync(v => v.Code == "USED")).IsActive);
        Assert.False(await db.Vouchers.AnyAsync(v => v.Code == "FRESH"));
    }

    [Theory]
    [InlineData("ab", false)]
    [InlineData("OK1", true)]
    [InlineData("WELCOME-20_x", true)]
    [InlineData("có dấu", false)]
    [InlineData("has space", false)]
    public void Voucher_code_format_is_validated(string code, bool valid)
    {
        var form = Form(null, code);
        var errors = form.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(form)).ToList();

        Assert.Equal(valid, errors.All(e => !e.MemberNames.Contains(nameof(VoucherFormModel.Code))));
    }

    [Fact]
    public void Voucher_form_expands_thousands_and_drops_a_cap_that_makes_no_sense_for_fixed_amounts()
    {
        var form = Form(null);
        form.DiscountKind = "amount";
        form.DiscountAmount = 30;      // gõ 30 = 30.000đ
        form.MaxDiscountAmount = 50;
        var v = new Voucher();

        form.ApplyTo(v);

        Assert.Equal(30_000m, v.DiscountAmount);
        Assert.Null(v.DiscountPercent);
        Assert.Null(v.MaxDiscountAmount);
    }
}

public class DemoPricingSeedTests
{
    [Fact]
    public async Task Seeding_gives_every_branch_a_pricing_and_prices_showtimes_by_room_and_time_not_by_movie()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var dbName = $"pricing-{Guid.NewGuid()}";
        services.AddDbContext<CinemaDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<CinemaDbContext>();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in AppRoles.All) await roles.CreateAsync(new IdentityRole(role));
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();

        await CatalogSeeder.SeedAsync(db);
        await DemoDataSeeder.SeedAsync(scope.ServiceProvider);

        Assert.Equal(await db.Theaters.CountAsync(), await db.TheaterPricings.CountAsync());
        Assert.True(await db.Vouchers.AnyAsync(v => v.Code == "CINE10"));
        Assert.True(await db.Vouchers.AnyAsync(v => v.Code == "HETHAN" && v.EndDate < DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7))));

        // Các suất do seed demo tạo cho cùng (chi nhánh, loại phòng, giờ, loại ngày) có cùng giá dù chiếu phim khác nhau.
        var pricings = await db.TheaterPricings.ToDictionaryAsync(p => p.TheaterId);
        var showtimes = await db.Showtimes.Include(s => s.Room).Where(s => s.Room!.Theater!.Name == "CineSmart Long Biên").Take(300).ToListAsync();
        Assert.NotEmpty(showtimes);
        foreach (var s in showtimes)
        {
            var expected = PricingRules.BasePrice(pricings[s.Room!.TheaterId], s.Room.RoomType, s.StartTimeUtc.AddHours(7));
            Assert.Equal(expected, s.BasePrice);
        }
    }
}

public class SurchargeSwitchTests
{
    private static CinemaBooking.Domain.Entities.TheaterPricing Pricing(decimal peak, decimal weekend)
    {
        var p = CinemaBooking.Domain.PricingRules.DefaultFor(1, 90_000m);
        p.PeakSurcharge = peak; p.WeekendSurcharge = weekend;
        return p;
    }

    [Theory]
    [InlineData(25_000, 10_000, 4)]
    [InlineData(25_000, 0, 2)]
    [InlineData(0, 10_000, 2)]
    [InlineData(0, 0, 1)]
    public void Matrix_only_lists_rows_that_still_differ(int peak, int weekend, int rows)
    {
        var m = CinemaBooking.Web.Models.PriceMatrixModel.Build(Pricing(peak, weekend), [CinemaBooking.Domain.Enums.RoomType.Standard]);
        Assert.Equal(rows, m.Tables[0].Rows.Count);
    }

    [Fact]
    public void Turning_both_off_gives_one_flat_price()
    {
        var p = Pricing(0, 0);
        var sat = new DateTime(2026, 11, 7, 20, 0, 0);
        var mon = new DateTime(2026, 11, 2, 10, 0, 0);
        Assert.Equal(CinemaBooking.Domain.PricingRules.BasePrice(p, CinemaBooking.Domain.Enums.RoomType.Standard, mon),
                     CinemaBooking.Domain.PricingRules.BasePrice(p, CinemaBooking.Domain.Enums.RoomType.Standard, sat));
    }
}
