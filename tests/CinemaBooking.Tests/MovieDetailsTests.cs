using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Controllers;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

public class MovieDetailsTests
{
    private static readonly DisplayTimeZone Tz = new() { TimeZoneOffsetHours = 7 };

    // 03:00Z = 10:00 giờ rạp ngày 10/10.
    private static readonly DateTime NowUtc = new(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc);

    private static MoviesController Sut(CinemaDbContext db)
    {
        var clock = new FixedTimeProvider(NowUtc);
        var options = Options.Create(Tz);
        return new MoviesController(db, new ShowtimeQueryService(db, options, clock), options, clock);
    }

    [Fact]
    public async Task Details_returns_404_for_a_hidden_movie()
    {
        using var db = TestDatabase.Create();
        var movie = db.SeedMovie("Đã gỡ", isActive: false);
        await db.SaveChangesAsync();

        var result = await Sut(db).Details(movie.Id, null, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Details_groups_showtimes_by_branch_and_hides_started_ones()
    {
        using var db = TestDatabase.Create();
        var q1 = db.SeedRoom("CineSmart Nguyễn Huệ");
        var hn = db.SeedRoom("CineSmart Cầu Giấy");
        var movie = db.SeedMovie("Dune");

        db.SeedShowtime(movie, q1, NowUtc.AddHours(-1));   // đã bắt đầu — không bán
        db.SeedShowtime(movie, q1, NowUtc.AddHours(4));
        db.SeedShowtime(movie, q1, NowUtc.AddHours(2));
        db.SeedShowtime(movie, hn, NowUtc.AddHours(3));
        await db.SaveChangesAsync();

        var view = Assert.IsType<ViewResult>(await Sut(db).Details(movie.Id, null, default));
        var model = Assert.IsType<MovieDetailsViewModel>(view.Model);

        Assert.Equal(new DateOnly(2026, 10, 10), model.SelectedDate);
        Assert.Equal(["CineSmart Cầu Giấy", "CineSmart Nguyễn Huệ"], model.Branches.Select(b => b.TheaterName));

        var q1Times = model.Branches[1].Showtimes.Select(s => s.StartLocal.Hour).ToList();
        Assert.Equal([12, 14], q1Times); // 05:00Z và 07:00Z theo giờ rạp, đã sắp theo giờ
    }

    [Fact]
    public async Task Details_falls_back_to_first_available_day_when_date_has_no_showtimes()
    {
        using var db = TestDatabase.Create();
        var room = db.SeedRoom();
        var movie = db.SeedMovie("Dune");
        db.SeedShowtime(movie, room, NowUtc.AddDays(2));
        await db.SaveChangesAsync();

        var view = Assert.IsType<ViewResult>(await Sut(db).Details(movie.Id, new DateOnly(2026, 10, 11), default));
        var model = Assert.IsType<MovieDetailsViewModel>(view.Model);

        Assert.Equal(new DateOnly(2026, 10, 12), model.SelectedDate);
        Assert.Single(Assert.Single(model.Branches).Showtimes);
    }
}
