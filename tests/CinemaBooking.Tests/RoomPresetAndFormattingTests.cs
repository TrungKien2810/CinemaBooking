using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CinemaBooking.Tests;

public class SeatLayoutPresetTests
{
    private static List<Seat> Generate(int rows, int cols, SeatLayoutPreset preset, int aisle = 0, RoomType type = RoomType.Standard) =>
        SeatGenerator.Generate(new Room { Id = 1, TotalRows = rows, TotalColumns = cols, RoomType = type }, preset, aisle);

    private static int Count(List<Seat> seats, SeatType t) => seats.Count(s => s.SeatType == t);

    [Fact]
    public void All_standard_has_no_vip_and_no_couple()
    {
        var seats = Generate(8, 12, SeatLayoutPreset.AllStandard);

        Assert.Equal(96, Count(seats, SeatType.Standard));
    }

    [Fact]
    public void Vip_middle_has_vip_rows_but_no_couple()
    {
        var seats = Generate(9, 10, SeatLayoutPreset.VipMiddle);

        Assert.Equal(0, Count(seats, SeatType.Couple));
        Assert.Equal(30, Count(seats, SeatType.Vip)); // hàng 4–6
    }

    [Fact]
    public void Couple_back_has_couple_last_row_and_no_vip()
    {
        var seats = Generate(6, 10, SeatLayoutPreset.CoupleBack);

        Assert.Equal(0, Count(seats, SeatType.Vip));
        Assert.Equal(10, Count(seats, SeatType.Couple));
        Assert.All(seats.Where(s => s.SeatType == SeatType.Couple), s => Assert.Equal(6, s.RowNumber));
    }

    [Fact]
    public void All_vip_is_every_seat()
    {
        Assert.All(Generate(5, 8, SeatLayoutPreset.AllVip), s => Assert.Equal(SeatType.Vip, s.SeatType));
    }

    [Theory]
    [InlineData(RoomType.Standard, SeatLayoutPreset.Classic)]
    [InlineData(RoomType.ThreeD, SeatLayoutPreset.VipMiddle)]
    [InlineData(RoomType.Imax, SeatLayoutPreset.VipMiddle)]
    [InlineData(RoomType.FourDx, SeatLayoutPreset.AllStandard)]
    public void Auto_picks_the_preset_by_room_type(RoomType type, SeatLayoutPreset expected)
    {
        Assert.Equal(expected, SeatLayoutPreset.Auto.Resolve(type));
        Assert.Equal(SeatLayoutPreset.AllVip, SeatLayoutPreset.AllVip.Resolve(type)); // chọn tay thì giữ nguyên
    }

    [Fact]
    public void Imax_auto_room_has_vip_but_no_couple_seats()
    {
        var seats = Generate(9, 12, SeatLayoutPreset.Auto, type: RoomType.Imax);

        Assert.True(Count(seats, SeatType.Vip) > 0);
        Assert.Equal(0, Count(seats, SeatType.Couple));
    }

    [Fact]
    public void Aisle_column_has_no_seats_and_numbering_stays_continuous_on_both_sides()
    {
        var seats = Generate(4, 11, SeatLayoutPreset.AllStandard, aisle: 5);

        Assert.DoesNotContain(seats, s => s.ColumnNumber == 6);
        Assert.Equal(4 * 10, seats.Count);
        Assert.Equal(4 * 10, SeatGenerator.SeatCount(4, 11, 5));
        Assert.Contains(seats, s => s.ColumnNumber == 5);
        Assert.Contains(seats, s => s.ColumnNumber == 7);
    }

    [Fact]
    public void Couple_row_next_to_an_aisle_still_pairs_up_inside_each_side()
    {
        // 9 cột, lối đi ở cột 5 → hai đoạn 4 ghế: đều ghép đủ 2 cặp, không có ghế đôi lẻ.
        var seats = Generate(3, 9, SeatLayoutPreset.CoupleBack, aisle: 4);

        Assert.Null(SeatPairing.ValidateLayout(seats));
        Assert.Equal(8, Count(seats.Where(s => s.RowNumber == 3).ToList(), SeatType.Couple));
    }

    [Fact]
    public void Odd_sized_runs_leave_the_extra_seat_standard()
    {
        // 8 cột, lối đi ở cột 4 → đoạn 3 ghế (1–3) và đoạn 4 ghế (5–8): đoạn lẻ dư ghế 3.
        var seats = Generate(2, 8, SeatLayoutPreset.CoupleBack, aisle: 3);

        Assert.Null(SeatPairing.ValidateLayout(seats));
        Assert.Equal(SeatType.Standard, seats.Single(s => s.RowNumber == 2 && s.ColumnNumber == 3).SeatType);
    }

    [Fact]
    public void Preview_shows_gap_cells_for_the_aisle_and_counts_only_real_seats()
    {
        var preview = SeatPreviewModel.For(3, 10, SeatLayoutPreset.AllStandard, aisleAfterColumn: 4);

        Assert.All(preview.Grid, row => Assert.Null(row[4]));
        Assert.Equal(27, preview.SeatTotal);
        Assert.True(preview.HasAisle);
    }

    [Fact]
    public async Task Creating_a_room_with_a_preset_and_aisle_saves_exactly_those_seats()
    {
        var db = TestDatabase.Create();
        var theater = new Theater { Name = "CineSmart", City = "Hà Nội" };
        db.Theaters.Add(theater);
        await db.SaveChangesAsync();
        var controller = new CinemaBooking.Web.Areas.Admin.Controllers.RoomsController(db, FakeBranchScope.Admin());

        await controller.Create(new RoomFormModel
        {
            TheaterId = theater.Id, RoomName = "Lounge", TotalRows = 4, TotalColumns = 9, RoomType = RoomType.Standard,
            LayoutPreset = SeatLayoutPreset.AllVip, AisleAfterColumn = 4,
        }, CancellationToken.None);

        var seats = await db.Seats.ToListAsync();
        Assert.Equal(32, seats.Count);
        Assert.All(seats, s => Assert.Equal(SeatType.Vip, s.SeatType));
        Assert.DoesNotContain(seats, s => s.ColumnNumber == 5);
    }
}

public class MoneyFormatTests
{
    [Theory]
    [InlineData(90000, "90.000 đ")]
    [InlineData(1525000, "1.525.000 đ")]
    [InlineData(0, "0 đ")]
    [InlineData(999, "999 đ")]
    public void Vnd_uses_dots_and_no_decimals_regardless_of_server_culture(decimal amount, string expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal(expected, amount.Vnd());
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(90, 90000)]
    [InlineData(92.5, 92500)]
    [InlineData(90000, 90000)]
    [InlineData(1000, 1000)]
    [InlineData(0, 0)]
    [InlineData(-5, -5)]
    public void Typed_prices_below_one_thousand_are_read_as_thousands(double typed, double expected)
    {
        Assert.Equal((decimal)expected, MoneyFormat.ExpandThousands((decimal)typed));
    }

    [Fact]
    public void Form_models_expand_thousands_before_validation_runs()
    {
        var showtime = new ShowtimeFormModel { BasePrice = 95 };
        var auto = new AutoPlanForm { BasePrice = 80, PrimeSurcharge = 20 };
        var promo = new PromotionFormModel { FixedPrice = 60 };

        Assert.Equal(95_000m, showtime.BasePrice);
        Assert.Equal(80_000m, auto.BasePrice);
        Assert.Equal(20_000m, auto.PrimeSurcharge);
        Assert.Equal(60_000m, promo.FixedPrice);
        Assert.Null(new PromotionFormModel { FixedPrice = null }.FixedPrice);
    }

    [Fact]
    public void Thousands_helper_round_trips_for_the_input_box()
    {
        Assert.Equal("90", 90_000m.ToThousands());
        Assert.Equal("92.5", 92_500m.ToThousands());
    }
}

public class DemoDataSeederTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var dbName = $"demo-{Guid.NewGuid()}";
        services.AddDbContext<CinemaDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<CinemaDbContext>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Seeding_fills_a_rich_catalog_and_is_safe_to_run_twice()
    {
        using var provider = BuildProvider();
        using (var scope = provider.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in AppRoles.All) await roles.CreateAsync(new IdentityRole(role));
            await CatalogSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<CinemaDbContext>());
            await DemoDataSeeder.SeedAsync(scope.ServiceProvider);
        }

        int showtimes, bookings;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();
            Assert.True(await db.Theaters.CountAsync() >= 9);
            Assert.True(await db.Movies.CountAsync() >= 15);
            Assert.Contains(await db.Rooms.Select(r => r.RoomType).Distinct().ToListAsync(), t => t == RoomType.Imax);
            Assert.Contains(await db.Rooms.Select(r => r.RoomType).Distinct().ToListAsync(), t => t == RoomType.FourDx);
            Assert.True(await db.Users.CountAsync() >= 25);

            showtimes = await db.Showtimes.CountAsync();
            bookings = await db.Bookings.CountAsync();
            Assert.True(showtimes > 2000);
            Assert.True(bookings > 5000);

            // Không có ghế bị bán hai lần cho cùng một suất.
            var duplicates = (await db.BookingSeats.Select(b => new { b.ShowtimeId, b.SeatId }).ToListAsync())
                .GroupBy(x => x).Count(g => g.Count() > 1);
            Assert.Equal(0, duplicates);

            // Có đơn đã soát vé, đơn đã hủy/hết hạn, và phim "sắp chiếu" chưa có suất.
            Assert.True(await db.Bookings.AnyAsync(b => b.CheckedInUtc != null));
            Assert.True(await db.Bookings.AnyAsync(b => b.Status == BookingStatus.Cancelled));
            var comingSoon = await db.Movies.Where(m => m.ReleaseDate > DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7))).ToListAsync();
            Assert.NotEmpty(comingSoon);
            foreach (var movie in comingSoon)
                Assert.DoesNotContain(await db.Showtimes.Where(s => s.MovieId == movie.Id).ToListAsync(),
                    s => DateOnly.FromDateTime(s.StartTimeUtc.AddHours(7)) < movie.ReleaseDate);

            // Không có hai suất chồng nhau trong cùng một phòng.
            var byRoom = (await db.Showtimes.Select(s => new { s.RoomId, s.StartTimeUtc, s.EndTimeUtc }).ToListAsync())
                .GroupBy(s => s.RoomId);
            foreach (var room in byRoom)
            {
                var ordered = room.OrderBy(s => s.StartTimeUtc).ToList();
                for (var i = 1; i < ordered.Count; i++)
                    Assert.True(ordered[i].StartTimeUtc >= ordered[i - 1].EndTimeUtc, $"phòng {room.Key} có suất chồng nhau");
            }
        }

        using (var scope = provider.CreateScope())
        {
            await DemoDataSeeder.SeedAsync(scope.ServiceProvider);
            var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();
            Assert.Equal(showtimes, await db.Showtimes.CountAsync());
            Assert.Equal(bookings, await db.Bookings.CountAsync());
        }
    }
}
