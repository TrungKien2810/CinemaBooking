using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Controllers;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

/// <summary>Các lỗi tìm ra khi review đợt thay đổi lớn — mỗi test khóa một lỗi để không tái diễn.</summary>
public class ReviewFixesTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<(CinemaDbContext Db, Room Room, Dictionary<string, Seat> Seats)> SeedWideRoomAsync()
    {
        var db = TestDatabase.Create();
        var theater = new Theater { Name = "CineSmart", City = "Hà Nội" };
        var room = new Room { Theater = theater, RoomName = "Phòng 1", TotalRows = 2, TotalColumns = 6 };
        db.Theaters.Add(theater);
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        db.Seats.AddRange(SeatGenerator.Generate(room));
        await db.SaveChangesAsync();
        var seats = (await db.Seats.Where(s => s.RoomId == room.Id).ToListAsync()).ToDictionary(s => s.Label);
        return (db, room, seats);
    }

    private static async Task BookAsync(CinemaDbContext db, Room room, params Seat[] seats)
    {
        var movie = db.SeedMovie("Mắt Biếc");
        await db.SaveChangesAsync();
        var start = DateTime.UtcNow.AddDays(2);
        var showtime = new Showtime
        {
            MovieId = movie.Id, RoomId = room.Id, BasePrice = 90_000m,
            StartTimeUtc = start, EndTimeUtc = start.AddHours(2),
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();
        db.Bookings.Add(new Booking
        {
            BookingCode = "CB-PAIR01", ShowtimeId = showtime.Id, CustomerName = "A", CustomerEmail = "a@b.c",
            Status = BookingStatus.Confirmed,
            Seats = seats.Select(s => new BookingSeat { SeatId = s.Id, ShowtimeId = showtime.Id, PriceAtBooking = 90_000m }).ToList(),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<SeatType> TypeOfAsync(CinemaDbContext db, int seatId)
    {
        db.ChangeTracker.Clear();
        return (await db.Seats.SingleAsync(s => s.Id == seatId)).SeatType;
    }

    [Fact]
    public async Task Editing_neighbours_cannot_re_pair_a_couple_that_already_has_a_ticket()
    {
        var (db, room, seats) = await SeedWideRoomAsync();
        // Hàng A: cặp A3+A4 đã bán.
        foreach (var label in new[] { "A3", "A4" }) seats[label].SeatType = SeatType.Couple;
        await db.SaveChangesAsync();
        await BookAsync(db, room, seats["A3"], seats["A4"]);
        var controller = new RoomsController(db, FakeBranchScope.Admin());

        // A2 + A5 thành ghế đôi → đoạn A2..A5 sẽ ghép (A2,A3)(A4,A5), làm vỡ cặp A3+A4 đã bán.
        await controller.Seats(room.Id, $"{seats["A2"].Id}=Couple;{seats["A5"].Id}=Couple", CancellationToken.None);

        Assert.Equal(SeatType.Vip, await TypeOfAsync(db, seats["A2"].Id));
        Assert.Equal(SeatType.Vip, await TypeOfAsync(db, seats["A5"].Id));
    }

    [Fact]
    public async Task Adding_a_whole_new_pair_next_to_a_sold_pair_is_still_allowed()
    {
        var (db, room, seats) = await SeedWideRoomAsync();
        foreach (var label in new[] { "A3", "A4" }) seats[label].SeatType = SeatType.Couple;
        await db.SaveChangesAsync();
        await BookAsync(db, room, seats["A3"], seats["A4"]);
        var controller = new RoomsController(db, FakeBranchScope.Admin());

        // A1+A2 thành cặp: đoạn A1..A4 vẫn ghép (A1,A2)(A3,A4) — cặp đã bán không đổi.
        await controller.Seats(room.Id, $"{seats["A1"].Id}=Couple;{seats["A2"].Id}=Couple", CancellationToken.None);

        Assert.Equal(SeatType.Couple, await TypeOfAsync(db, seats["A1"].Id));
        Assert.Equal(SeatType.Couple, await TypeOfAsync(db, seats["A2"].Id));
    }

    [Fact]
    public async Task Copying_a_day_keeps_the_language_of_each_showtime()
    {
        var (db, room, _) = await SeedWideRoomAsync();
        var movie = db.SeedMovie("Dune", durationMinutes: 100);
        await db.SaveChangesAsync();

        var source = new DateOnly(2026, 10, 5);
        var startUtc = source.ToDateTime(new TimeOnly(10, 0)) - TimeSpan.FromHours(7);
        db.Showtimes.Add(new Showtime
        {
            MovieId = movie.Id, RoomId = room.Id, BasePrice = 90_000m, Language = ShowLanguage.Dubbed,
            StartTimeUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            EndTimeUtc = DateTime.SpecifyKind(startUtc.AddMinutes(100), DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();

        var controller = new ShowtimesController(
            db, new ShowtimeScheduler(db), Options.Create(new DisplayTimeZone()), new FixedTimeProvider(Now), FakeBranchScope.Admin());
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            new Microsoft.AspNetCore.Http.DefaultHttpContext(), new FakeTempDataProvider());

        await controller.CopyDay(source, new DateOnly(2026, 10, 6), room.TheaterId, CancellationToken.None);

        var shows = await db.Showtimes.OrderBy(s => s.StartTimeUtc).ToListAsync();
        Assert.Equal(2, shows.Count);
        Assert.All(shows, s => Assert.Equal(ShowLanguage.Dubbed, s.Language));
    }

    [Fact]
    public void A_one_hundred_percent_promotion_can_never_make_a_free_ticket()
    {
        var promo = new Promotion
        {
            Title = "Free", IsActive = true, DiscountPercent = 100,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        };

        var quote = PromotionRules.Quote(90_000m, SeatType.Standard, new DateTime(2026, 10, 6, 19, 0, 0), [promo]);

        Assert.True(quote.FinalPrice > 0);
        Assert.Equal(900m, quote.FinalPrice);
    }

    [Fact]
    public async Task Auto_planner_ranks_rooms_by_real_seat_count_not_rows_times_columns()
    {
        // Phòng "lớn" 10x10 nhưng chỉ còn 20 ghế thật; phòng 5x5 có đủ 25 ghế → phòng 5x5 mới là phòng lớn hơn.
        var db = TestDatabase.Create();
        var theater = new Theater { Name = "CineSmart", City = "Hà Nội" };
        var big = new Room { Theater = theater, RoomName = "Trên giấy lớn", TotalRows = 10, TotalColumns = 10 };
        var real = new Room { Theater = theater, RoomName = "Thật sự lớn", TotalRows = 5, TotalColumns = 5 };
        db.Theaters.Add(theater);
        db.Rooms.AddRange(big, real);
        await db.SaveChangesAsync();
        db.Seats.AddRange(SeatGenerator.Generate(real));
        db.Seats.AddRange(SeatGenerator.Generate(big).Take(20));
        await db.SaveChangesAsync();

        var rooms = await db.Rooms.Where(r => r.TheaterId == theater.Id)
            .Select(r => new PlanRoom(r.Id, r.RoomName, r.Seats.Count)).ToListAsync();

        Assert.Equal("Thật sự lớn", rooms.OrderByDescending(r => r.Capacity).First().Name);
    }
}

internal sealed class FakeTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(Microsoft.AspNetCore.Http.HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(Microsoft.AspNetCore.Http.HttpContext context, IDictionary<string, object> values) { }
}
