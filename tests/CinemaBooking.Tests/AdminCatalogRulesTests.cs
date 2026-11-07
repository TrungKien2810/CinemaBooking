using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Controllers;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

public class AdminCatalogRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Suất có sẵn: 03:00 UTC = 10:00 giờ rạp, phim 120' → kết thúc 12:00 giờ rạp.</summary>
    private static readonly DateTime ExistingStartUtc = new(2026, 10, 2, 3, 0, 0, DateTimeKind.Utc);

    private static ShowtimesController CreateController(CinemaDbContext db) =>
        new(db, new ShowtimeScheduler(db), Options.Create(new DisplayTimeZone()), new FixedTimeProvider(Now),
            FakeBranchScope.Admin());

    private static async Task<(CinemaDbContext Db, Movie Movie, Room Room)> SeedWithShowtimeAsync()
    {
        var db = TestDatabase.Create();
        var room = db.SeedRoom();
        var movie = db.SeedMovie("Mắt Biếc", durationMinutes: 120);
        db.SeedShowtime(movie, room, ExistingStartUtc);
        await db.SaveChangesAsync();
        return (db, movie, room);
    }

    private static ShowtimeFormModel Form(Movie movie, Room room, int localHour, int localMinute = 0) => new()
    {
        MovieId = movie.Id,
        RoomId = room.Id,
        StartTimeLocal = new DateTime(2026, 10, 2, localHour, localMinute, 0),
        BasePrice = 90_000m,
    };

    [Fact]
    public async Task Create_rejects_a_showtime_overlapping_another_in_the_same_room()
    {
        var (db, movie, room) = await SeedWithShowtimeAsync();
        var controller = CreateController(db);

        var result = await controller.Create(Form(movie, room, 11), CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(ShowtimeFormModel.StartTimeLocal)));
        Assert.Equal(1, await db.Showtimes.CountAsync());
    }

    [Theory]
    [InlineData(12, 0)]   // chạm đúng giờ kết thúc — chưa kịp dọn phòng
    [InlineData(12, 10)]  // còn trong 15' dọn phòng
    [InlineData(8, 0)]    // suất mới kết thúc 10:00, không chừa thời gian dọn trước suất có sẵn
    public async Task Create_rejects_a_showtime_inside_the_cleaning_buffer(int hour, int minute)
    {
        var (db, movie, room) = await SeedWithShowtimeAsync();
        var controller = CreateController(db);

        await controller.Create(Form(movie, room, hour, minute), CancellationToken.None);

        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(1, await db.Showtimes.CountAsync());
    }

    [Theory]
    [InlineData(12, 15)]  // đủ 15' dọn phòng sau suất có sẵn
    [InlineData(7, 45)]   // kết thúc 9:45, dọn xong đúng 10:00
    public async Task Create_accepts_a_showtime_that_touches_the_cleaning_buffer_edge(int hour, int minute)
    {
        var (db, movie, room) = await SeedWithShowtimeAsync();
        var controller = CreateController(db);

        var result = await controller.Create(Form(movie, room, hour, minute), CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(2, await db.Showtimes.CountAsync());
    }

    [Fact]
    public async Task Create_accepts_the_same_time_in_another_room()
    {
        var (db, movie, _) = await SeedWithShowtimeAsync();
        var otherRoom = db.SeedRoom(theaterName: "CineSmart Hoàn Kiếm");
        await db.SaveChangesAsync();

        var result = await CreateController(db).Create(Form(movie, otherRoom, 10), CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(2, await db.Showtimes.CountAsync());
    }

    [Fact]
    public async Task Create_stores_start_in_utc_and_derives_end_from_movie_duration()
    {
        var (db, movie, room) = await SeedWithShowtimeAsync();

        await CreateController(db).Create(Form(movie, room, 19), CancellationToken.None);

        var created = await db.Showtimes.OrderBy(s => s.Id).LastAsync();
        Assert.Equal(new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc), created.StartTimeUtc);
        Assert.Equal(created.StartTimeUtc.AddMinutes(120), created.EndTimeUtc);
    }

    [Fact]
    public async Task Edit_does_not_count_the_showtime_as_overlapping_itself()
    {
        var (db, movie, room) = await SeedWithShowtimeAsync();
        var existing = await db.Showtimes.SingleAsync();
        var form = Form(movie, room, 10, 30);

        var result = await CreateController(db).Edit(existing.Id, form, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(new DateTime(2026, 10, 2, 3, 30, 0, DateTimeKind.Utc), (await db.Showtimes.SingleAsync()).StartTimeUtc);
    }

    [Fact]
    public async Task Create_rejects_a_showtime_in_the_past()
    {
        var (db, movie, room) = await SeedWithShowtimeAsync();
        var form = Form(movie, room, 15);
        form.StartTimeLocal = new DateTime(2026, 9, 1, 15, 0, 0); // trước Now (01/10/2026)
        var controller = CreateController(db);

        var result = await controller.Create(form, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(ShowtimeFormModel.StartTimeLocal)));
        Assert.Equal(1, await db.Showtimes.CountAsync());
    }

    [Fact]
    public async Task Editing_only_the_price_of_a_started_showtime_is_allowed()
    {
        var (db, movie, room) = await SeedWithShowtimeAsync();
        var existing = await db.Showtimes.SingleAsync();
        // Giờ hiện tại đã qua giờ bắt đầu của suất có sẵn.
        var controller = new ShowtimesController(
            db, new ShowtimeScheduler(db), Options.Create(new DisplayTimeZone()),
            new FixedTimeProvider(ExistingStartUtc.AddHours(1)), FakeBranchScope.Admin());
        var form = Form(movie, room, 10);
        form.BasePrice = 120_000m;

        var result = await controller.Edit(existing.Id, form, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(120_000m, (await db.Showtimes.SingleAsync()).BasePrice);
    }

    [Fact]
    public async Task Deleting_a_showtime_that_has_bookings_is_refused()
    {
        var (db, _, _) = await SeedWithShowtimeAsync();
        var showtime = await db.Showtimes.SingleAsync();
        db.Bookings.Add(new Booking
        {
            BookingCode = "CB-DEL001", ShowtimeId = showtime.Id,
            CustomerName = "Khách", CustomerEmail = "khach@example.com",
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            new Microsoft.AspNetCore.Http.DefaultHttpContext(), new NullTempDataProvider());
        await controller.Delete(showtime.Id, CancellationToken.None);

        Assert.Equal(1, await db.Showtimes.CountAsync());
    }

    private static ShowtimesController WithTempData(ShowtimesController c)
    {
        c.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            new Microsoft.AspNetCore.Http.DefaultHttpContext(), new NullTempDataProvider());
        return c;
    }

    [Fact]
    public async Task CopyDay_duplicates_showtimes_to_the_target_day_and_skips_clashes()
    {
        var (db, _, _) = await SeedWithShowtimeAsync();
        var controller = WithTempData(CreateController(db));
        var source = new DateOnly(2026, 10, 2);
        var target = new DateOnly(2026, 10, 3);

        await controller.CopyDay(source, target, null, CancellationToken.None);
        Assert.Equal(2, await db.Showtimes.CountAsync());
        Assert.Equal(ExistingStartUtc.AddDays(1), (await db.Showtimes.OrderBy(s => s.Id).LastAsync()).StartTimeUtc);

        await controller.CopyDay(source, target, null, CancellationToken.None); // lần hai: trùng lịch hết
        Assert.Equal(2, await db.Showtimes.CountAsync());
    }

    [Fact]
    public async Task ToggleActive_hides_and_shows_a_showtime()
    {
        var (db, _, _) = await SeedWithShowtimeAsync();
        var controller = WithTempData(CreateController(db));
        var id = (await db.Showtimes.SingleAsync()).Id;

        await controller.ToggleActive(id, CancellationToken.None);
        Assert.False((await db.Showtimes.SingleAsync()).IsActive);

        await controller.ToggleActive(id, CancellationToken.None);
        Assert.True((await db.Showtimes.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Creating_a_room_with_a_taken_name_in_the_same_branch_is_a_validation_error()
    {
        var db = TestDatabase.Create();
        var room = db.SeedRoom(roomName: "Phòng 1");
        await db.SaveChangesAsync();
        var controller = new RoomsController(db, FakeBranchScope.Admin());

        var result = await controller.Create(
            new RoomFormModel { TheaterId = room.TheaterId, RoomName = " Phòng 1 ", TotalRows = 3, TotalColumns = 4 },
            CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(RoomFormModel.RoomName)));
        Assert.Equal(1, await db.Rooms.CountAsync());
    }

    [Fact]
    public async Task Create_rejects_an_inactive_movie_posted_by_a_tampered_form()
    {
        var (db, _, room) = await SeedWithShowtimeAsync();
        var hidden = db.SeedMovie("Phim đã ẩn", isActive: false);
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        await controller.Create(Form(hidden, room, 19), CancellationToken.None);

        Assert.True(controller.ModelState.ContainsKey(nameof(ShowtimeFormModel.MovieId)));
        Assert.Equal(1, await db.Showtimes.CountAsync());
    }

    [Fact]
    public async Task Create_rejects_a_room_of_a_closed_branch()
    {
        var (db, movie, room) = await SeedWithShowtimeAsync();
        (await db.Theaters.SingleAsync()).IsActive = false;
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        await controller.Create(Form(movie, room, 19), CancellationToken.None);

        Assert.True(controller.ModelState.ContainsKey(nameof(ShowtimeFormModel.RoomId)));
    }

    [Fact]
    public async Task Deleting_a_movie_only_hides_it_and_keeps_its_showtimes()
    {
        var (db, movie, _) = await SeedWithShowtimeAsync();

        await new MoviesController(db).Delete(movie.Id, CancellationToken.None);

        var stored = await db.Movies.SingleAsync();
        Assert.False(stored.IsActive);
        Assert.Equal(1, await db.Showtimes.CountAsync(s => s.MovieId == movie.Id));
    }

    [Fact]
    public async Task Deleting_a_branch_only_closes_it_and_keeps_its_rooms()
    {
        var (db, _, room) = await SeedWithShowtimeAsync();

        await new TheatersController(db).Delete(room.TheaterId, CancellationToken.None);

        Assert.False((await db.Theaters.SingleAsync()).IsActive);
        Assert.Equal(1, await db.Rooms.CountAsync());
    }

    [Fact]
    public async Task Creating_a_branch_with_a_taken_name_is_a_validation_error()
    {
        var db = TestDatabase.Create();
        db.SeedRoom(theaterName: "CineSmart Nguyễn Huệ");
        await db.SaveChangesAsync();
        var controller = new TheatersController(db);

        var result = await controller.Create(
            new TheaterFormModel { Name = " CineSmart Nguyễn Huệ ", City = "TP. Hồ Chí Minh" }, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(TheaterFormModel.Name)));
    }

    [Fact]
    public async Task Changing_room_layout_regenerates_every_seat()
    {
        // SQLite thật: xóa ghế cũ và thêm ghế mới cùng (RoomId, Row, Column) trong một SaveChanges
        // phải qua được unique index — InMemory không kiểm tra điều đó.
        using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        using var db = new CinemaDbContext(new DbContextOptionsBuilder<CinemaDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var room = new Room { RoomName = "Phòng 1", TotalRows = 3, TotalColumns = 4 };
        db.Theaters.Add(new Theater { Name = "CineSmart", City = "Hà Nội", Rooms = { room } });
        await db.SaveChangesAsync();
        db.Seats.AddRange(SeatGenerator.Generate(room));
        await db.SaveChangesAsync();

        var form = new RoomFormModel { RoomName = "Phòng 1", TotalRows = 5, TotalColumns = 6 };
        var result = await new RoomsController(db, FakeBranchScope.Admin()).Edit(room.Id, form, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(30, await db.Seats.CountAsync(s => s.RoomId == room.Id));
        Assert.Equal((5, 6), (room.TotalRows, room.TotalColumns));
    }

    [Fact]
    public async Task Changing_room_layout_is_rejected_once_a_seat_is_booked()
    {
        var db = TestDatabase.Create();
        var room = db.SeedRoom();
        var movie = db.SeedMovie("Mắt Biếc");
        var showtime = db.SeedShowtime(movie, room, ExistingStartUtc);
        await db.SaveChangesAsync();
        var seat = SeatGenerator.Generate(room).First();
        db.Seats.Add(seat);
        db.Bookings.Add(new Booking
        {
            BookingCode = "CB-TEST01",
            Showtime = showtime,
            CustomerName = "Khách",
            CustomerEmail = "khach@example.com",
            Seats = { new BookingSeat { Seat = seat, ShowtimeId = showtime.Id } },
        });
        await db.SaveChangesAsync();
        var controller = new RoomsController(db, FakeBranchScope.Admin());

        var result = await controller.Edit(
            room.Id, new RoomFormModel { RoomName = room.RoomName, TotalRows = 2, TotalColumns = 2 }, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(5, (await db.Rooms.SingleAsync()).TotalRows);
    }
}

file sealed class NullTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(Microsoft.AspNetCore.Http.HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(Microsoft.AspNetCore.Http.HttpContext context, IDictionary<string, object> values) { }
}
