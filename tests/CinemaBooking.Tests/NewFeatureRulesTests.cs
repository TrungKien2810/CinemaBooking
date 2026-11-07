using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Tests;

/// <summary>Khuyến mãi tự áp giá, ghép ghế đôi linh hoạt, che dữ liệu cá nhân, soát vé và xếp lịch tự động.</summary>
public class PromotionRulesTests
{
    // Thứ Ba 06/10/2026, 19:00 giờ rạp.
    private static readonly DateTime TuesdayEvening = new(2026, 10, 6, 19, 0, 0);

    private static Promotion Promo(Action<Promotion>? tweak = null)
    {
        var p = new Promotion
        {
            Title = "KM", IsActive = true,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31),
            DiscountPercent = 20,
        };
        tweak?.Invoke(p);
        return p;
    }

    [Fact]
    public void Percent_discount_reduces_the_price_and_names_the_promotion()
    {
        var q = PromotionRules.Quote(100_000m, SeatType.Standard, TuesdayEvening, [Promo()]);

        Assert.Equal(80_000m, q.FinalPrice);
        Assert.Equal(20_000m, q.Discount);
        Assert.Equal("KM", q.PromotionTitle);
    }

    [Fact]
    public void Fixed_price_never_raises_a_cheaper_seat()
    {
        var promo = Promo(p => { p.DiscountPercent = null; p.FixedPrice = 60_000m; });

        Assert.Equal(60_000m, PromotionRules.Quote(95_000m, SeatType.Standard, TuesdayEvening, [promo]).FinalPrice);
        Assert.Equal(50_000m, PromotionRules.Quote(50_000m, SeatType.Standard, TuesdayEvening, [promo]).FinalPrice);
    }

    [Fact]
    public void Only_the_best_promotion_applies_and_they_do_not_stack()
    {
        var q = PromotionRules.Quote(100_000m, SeatType.Standard, TuesdayEvening,
            [Promo(p => p.DiscountPercent = 10), Promo(p => { p.DiscountPercent = 30; p.Title = "Tốt nhất"; })]);

        Assert.Equal(70_000m, q.FinalPrice);
        Assert.Equal("Tốt nhất", q.PromotionTitle);
    }

    [Theory]
    [InlineData(2, true)]   // Thứ Ba
    [InlineData(3, false)]  // Thứ Tư
    public void Day_of_week_mask_limits_the_days(int dayOfWeek, bool applies)
    {
        var promo = Promo(p => p.DaysOfWeekMask = 1 << (int)DayOfWeek.Tuesday);
        var day = TuesdayEvening.AddDays(dayOfWeek - (int)DayOfWeek.Tuesday);

        Assert.Equal(applies, PromotionRules.Applies(promo, day, SeatType.Standard));
    }

    [Fact]
    public void Time_window_uses_the_showtime_start_time()
    {
        var early = Promo(p => p.ToTime = new TimeOnly(12, 0));
        var evening = Promo(p => p.FromTime = new TimeOnly(18, 0));

        Assert.False(PromotionRules.Applies(early, TuesdayEvening, SeatType.Standard));
        Assert.True(PromotionRules.Applies(early, TuesdayEvening.Date.AddHours(9), SeatType.Standard));
        Assert.True(PromotionRules.Applies(evening, TuesdayEvening, SeatType.Standard));
        // Đúng mốc kết thúc thì không còn thuộc khung (nửa khoảng [từ, đến)).
        Assert.False(PromotionRules.Applies(early, TuesdayEvening.Date.AddHours(12), SeatType.Standard));
    }

    [Fact]
    public void Informational_or_inactive_promotions_never_change_the_price()
    {
        var info = Promo(p => p.DiscountPercent = null);
        var off = Promo(p => p.IsActive = false);

        Assert.False(PromotionRules.Quote(100_000m, SeatType.Standard, TuesdayEvening, [info, off]).HasDiscount);
    }

    [Fact]
    public void Seat_type_filter_is_respected()
    {
        var promo = Promo(p => p.ApplicableSeatType = SeatType.Couple);

        Assert.False(PromotionRules.Applies(promo, TuesdayEvening, SeatType.Standard));
        Assert.True(PromotionRules.Applies(promo, TuesdayEvening, SeatType.Couple));
    }
}

public class FlexibleCouplePairingTests
{
    private static Seat S(int id, int row, int col, SeatType type) =>
        new() { Id = id, RowNumber = row, ColumnNumber = col, SeatType = type };

    [Fact]
    public void A_lone_couple_seat_does_not_shift_the_pairing_of_the_rest_of_the_row()
    {
        // Cột 1 lẻ loi (cột 2 là ghế thường), cột 3–4 là một cặp.
        var seats = new[]
        {
            S(1, 2, 1, SeatType.Couple), S(2, 2, 2, SeatType.Standard),
            S(3, 2, 3, SeatType.Couple), S(4, 2, 4, SeatType.Couple),
        };

        var partners = SeatPairing.BuildPartnerMap(seats);

        Assert.Equal(4, partners[3]);
        Assert.Equal(3, partners[4]);
        Assert.False(partners.ContainsKey(1));
        Assert.Equal(["B1"], SeatPairing.OrphanCoupleSeats(seats).Select(s => s.Label));
    }

    [Fact]
    public void Couple_pairs_can_sit_in_any_row_and_several_per_row()
    {
        var seats = new[]
        {
            S(1, 1, 2, SeatType.Couple), S(2, 1, 3, SeatType.Couple),
            S(3, 1, 4, SeatType.Couple), S(4, 1, 5, SeatType.Couple),
        };

        var partners = SeatPairing.BuildPartnerMap(seats);

        Assert.Equal(2, partners[1]);
        Assert.Equal(4, partners[3]);
        Assert.Null(SeatPairing.ValidateLayout(seats));
    }

    [Fact]
    public void Layout_validation_names_the_lone_seats_but_tolerates_ones_that_already_existed()
    {
        var seats = new[] { S(1, 1, 1, SeatType.Couple), S(2, 1, 5, SeatType.Couple) };

        var error = SeatPairing.ValidateLayout(seats);
        Assert.NotNull(error);
        Assert.Contains("A1", error);
        Assert.Contains("A5", error);

        Assert.Null(SeatPairing.ValidateLayout(seats, new HashSet<int> { 1, 2 }));
    }

    [Fact]
    public void Default_layout_with_odd_columns_keeps_couple_seats_in_whole_pairs()
    {
        var seats = SeatGenerator.Generate(new Room { Id = 1, TotalRows = 3, TotalColumns = 5 });

        Assert.Null(SeatPairing.ValidateLayout(seats));
        Assert.Equal(SeatType.Standard, seats.Single(s => s.RowNumber == 3 && s.ColumnNumber == 5).SeatType);
        Assert.Equal(4, seats.Count(s => s.SeatType == SeatType.Couple));
    }
}

public class PersonalDataMaskTests
{
    [Fact]
    public void Masks_name_email_and_phone()
    {
        Assert.Equal("N*** V*** A***", PersonalDataMask.Name("Nguyễn Văn An"));
        Assert.Equal("a***@example.com", PersonalDataMask.Email("an.nguyen@example.com"));
        Assert.Equal("*******567", PersonalDataMask.Phone("0901234567"));
        Assert.Null(PersonalDataMask.Phone(null));
    }

    [Fact]
    public void Masked_values_never_contain_the_original_local_part_or_digits()
    {
        var email = PersonalDataMask.Email("secret.person@example.com");
        Assert.DoesNotContain("secret", email);
        Assert.DoesNotContain("0901", PersonalDataMask.Phone("0901234567")!);
    }
}

public class TicketCheckInTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 16, 10, 0, 0, DateTimeKind.Utc);
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private CinemaDbContext _db = null!;
    private Booking _booking = null!;
    private Showtime _showtime = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new CinemaDbContext(new DbContextOptionsBuilder<CinemaDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();

        var theater = new Theater { Name = "CineSmart", City = "Hà Nội" };
        var room = new Room { Theater = theater, RoomName = "Phòng 1", TotalRows = 2, TotalColumns = 2 };
        var movie = new Movie { Title = "Mắt Biếc", DurationMinutes = 120 };
        _db.AddRange(theater, room, movie);
        await _db.SaveChangesAsync();

        _showtime = new Showtime
        {
            Movie = movie, Room = room, BasePrice = 90_000m,
            StartTimeUtc = Now.AddMinutes(30), EndTimeUtc = Now.AddMinutes(150),
        };
        _db.Showtimes.Add(_showtime);
        _booking = new Booking
        {
            BookingCode = "CB-CHECK1", Showtime = _showtime, CustomerName = "An", CustomerEmail = "an@x.vn",
            Status = BookingStatus.Confirmed, BookingTimeUtc = Now.AddDays(-1),
        };
        _db.Bookings.Add(_booking);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private TicketCheckInService Service(DateTime? now = null) =>
        new(_db, new FixedTimeProvider(now ?? Now));

    [Fact]
    public async Task A_valid_ticket_checks_in_once_and_the_second_scan_is_refused()
    {
        var first = await Service().CheckInAsync("cb-check1", "staff@cinema.vn");
        var second = await Service().CheckInAsync("CB-CHECK1", "other@cinema.vn");

        Assert.Equal(CheckInOutcome.Ok, first);
        Assert.Equal(CheckInOutcome.AlreadyCheckedIn, second);

        _db.ChangeTracker.Clear();
        var saved = await _db.Bookings.SingleAsync();
        Assert.Equal(Now, saved.CheckedInUtc);
        // Người soát đầu tiên được giữ nguyên, lần quét thứ hai không ghi đè.
        Assert.Equal("staff@cinema.vn", saved.CheckedInBy);
    }

    [Fact]
    public async Task Gate_opens_an_hour_before_the_show_and_closes_when_it_ends()
    {
        // Suất bắt đầu sau 30 phút → đang trong cửa sổ. Lùi giờ để quá sớm / quá muộn.
        Assert.Equal(CheckInOutcome.TooEarly, await Service(Now.AddHours(-2)).CheckInAsync("CB-CHECK1", "s"));
        Assert.Equal(CheckInOutcome.ShowEnded, await Service(Now.AddHours(5)).CheckInAsync("CB-CHECK1", "s"));

        _db.ChangeTracker.Clear();
        Assert.Null((await _db.Bookings.SingleAsync()).CheckedInUtc);
    }

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Expired)]
    public async Task Unpaid_or_cancelled_bookings_cannot_check_in(BookingStatus status)
    {
        var booking = await _db.Bookings.SingleAsync();
        booking.Status = status;
        await _db.SaveChangesAsync();

        Assert.Equal(CheckInOutcome.NotPaid, await Service().CheckInAsync("CB-CHECK1", "s"));
    }

    [Fact]
    public async Task Unknown_code_is_not_found()
    {
        Assert.Equal(CheckInOutcome.NotFound, await Service().CheckInAsync("CB-NOPE00", "s"));
    }
}

public class ShowtimeAutoPlannerTests
{
    private static readonly DateOnly Day = new(2026, 10, 20);
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<(DateTime StartLocal, DateTime EndLocal)>> NoBusy =
        new Dictionary<int, IReadOnlyList<(DateTime, DateTime)>>();

    private static readonly PlanRoom Big = new(1, "Phòng lớn", 140);
    private static readonly PlanRoom Small = new(2, "Phòng nhỏ", 40);
    private static readonly PlanMovie Hot = new(10, "Phim mới", 120, new DateOnly(2026, 10, 15));
    private static readonly PlanMovie Old = new(11, "Phim cũ", 100, new DateOnly(2025, 1, 1));
    private static readonly PlanMovie Mid = new(12, "Phim vừa", 110, new DateOnly(2025, 8, 1));

    [Fact]
    public void Hottest_movie_gets_the_biggest_room_in_prime_time()
    {
        var plan = ShowtimeAutoPlanner.Plan(Day, [Small, Big], [Old, Hot, Mid], NoBusy, TimeSpan.FromMinutes(15));

        var primeInBig = plan.Where(p => p.Prime && p.RoomId == Big.RoomId).OrderBy(p => p.StartLocal).ToList();
        Assert.NotEmpty(primeInBig);
        // Suất giờ vàng đầu tiên của phòng lớn nhất là phim hot nhất (các suất sau xen phim khác để không lặp liền nhau).
        Assert.Equal(Hot.MovieId, primeInBig[0].MovieId);

        // Cùng khung giờ vàng, phòng nhỏ nhận phim kế tiếp — hai phòng không chiếu cùng một phim.
        var primeInSmall = plan.Where(p => p.Prime && p.RoomId == Small.RoomId).OrderBy(p => p.StartLocal).ToList();
        Assert.Equal(Mid.MovieId, primeInSmall[0].MovieId);
    }

    [Fact]
    public void Shows_in_a_room_never_overlap_and_keep_the_cleaning_buffer()
    {
        var buffer = TimeSpan.FromMinutes(15);
        var plan = ShowtimeAutoPlanner.Plan(Day, [Big, Small], [Hot, Old], NoBusy, buffer);

        foreach (var room in plan.GroupBy(p => p.RoomId))
        {
            var ordered = room.OrderBy(p => p.StartLocal).ToList();
            for (var i = 1; i < ordered.Count; i++)
                Assert.True(ordered[i].StartLocal >= ordered[i - 1].EndLocal + buffer);
        }

        Assert.All(plan, p => Assert.True(p.StartLocal.TimeOfDay <= ShowtimeAutoPlanner.LastStart));
        Assert.All(plan, p => Assert.True(p.StartLocal.TimeOfDay >= ShowtimeAutoPlanner.Opening));
    }

    [Fact]
    public void Existing_showtimes_are_never_overlapped()
    {
        var busy = new Dictionary<int, IReadOnlyList<(DateTime StartLocal, DateTime EndLocal)>>
        {
            [Big.RoomId] = [(Day.ToDateTime(new TimeOnly(18, 0)), Day.ToDateTime(new TimeOnly(21, 0)))],
        };

        var plan = ShowtimeAutoPlanner.Plan(Day, [Big], [Hot, Old], busy, TimeSpan.FromMinutes(15));

        Assert.DoesNotContain(plan, p =>
            p.StartLocal < Day.ToDateTime(new TimeOnly(21, 15)) && p.EndLocal > Day.ToDateTime(new TimeOnly(17, 45)));
    }

    [Fact]
    public void Same_movie_is_not_repeated_back_to_back_in_a_room()
    {
        var plan = ShowtimeAutoPlanner.Plan(Day, [Big], [Hot, Old], NoBusy, TimeSpan.FromMinutes(15));
        var ordered = plan.OrderBy(p => p.StartLocal).ToList();

        for (var i = 1; i < ordered.Count; i++) Assert.NotEqual(ordered[i - 1].MovieId, ordered[i].MovieId);
    }

    [Fact]
    public void Planning_today_never_starts_before_the_current_time()
    {
        var now = Day.ToDateTime(new TimeOnly(14, 7));
        var plan = ShowtimeAutoPlanner.Plan(Day, [Big], [Hot, Old], NoBusy, TimeSpan.FromMinutes(15), notBeforeLocal: now);

        Assert.All(plan, p => Assert.True(p.StartLocal >= now));
        Assert.All(plan, p => Assert.Equal(0, p.StartLocal.Minute % 5));
    }

    [Fact]
    public void No_rooms_or_no_movies_gives_an_empty_plan()
    {
        Assert.Empty(ShowtimeAutoPlanner.Plan(Day, [], [Hot], NoBusy, TimeSpan.FromMinutes(15)));
        Assert.Empty(ShowtimeAutoPlanner.Plan(Day, [Big], [], NoBusy, TimeSpan.FromMinutes(15)));
    }
}
