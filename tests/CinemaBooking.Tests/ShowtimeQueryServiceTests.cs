using CinemaBooking.Web.Services;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

public class ShowtimeQueryServiceTests
{
    // Giờ rạp = UTC+7, nên 17:00Z ngày 09/10 đã là 00:00 ngày 10/10 ở Việt Nam.
    private static readonly DisplayTimeZone Tz = new() { TimeZoneOffsetHours = 7 };

    private static ShowtimeQueryService Sut(Infrastructure.Persistence.CinemaDbContext db, DateTime nowUtc)
        => new(db, Options.Create(Tz), new FixedTimeProvider(nowUtc));

    [Fact]
    public async Task GetForMovieOnDate_uses_cinema_local_day_not_utc_day()
    {
        using var db = TestDatabase.Create();
        var room = db.SeedRoom();
        var movie = db.SeedMovie("Dune");

        // 17:00Z ngày 09/10 ⇒ 00:00 ngày 10/10 giờ rạp: phải thuộc ngày 10, không phải ngày 09.
        db.SeedShowtime(movie, room, new DateTime(2026, 10, 9, 17, 0, 0, DateTimeKind.Utc));
        // 16:00Z ngày 09/10 ⇒ 23:00 ngày 09/10 giờ rạp.
        db.SeedShowtime(movie, room, new DateTime(2026, 10, 9, 16, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var sut = Sut(db, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        var oct10 = await sut.GetForMovieOnDateAsync(movie.Id, new DateOnly(2026, 10, 10));
        var oct09 = await sut.GetForMovieOnDateAsync(movie.Id, new DateOnly(2026, 10, 9));

        Assert.Equal(new DateTime(2026, 10, 9, 17, 0, 0, DateTimeKind.Utc), Assert.Single(oct10).StartTimeUtc);
        Assert.Equal(new DateTime(2026, 10, 9, 16, 0, 0, DateTimeKind.Utc), Assert.Single(oct09).StartTimeUtc);
    }

    [Fact]
    public async Task GetForMovieOnDate_excludes_other_movies_and_sorts_by_start_time()
    {
        using var db = TestDatabase.Create();
        var room = db.SeedRoom();
        var dune = db.SeedMovie("Dune");
        var other = db.SeedMovie("Mai");

        db.SeedShowtime(dune, room, new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc));
        db.SeedShowtime(dune, room, new DateTime(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc));
        db.SeedShowtime(other, room, new DateTime(2026, 10, 10, 4, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var sut = Sut(db, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await sut.GetForMovieOnDateAsync(dune.Id, new DateOnly(2026, 10, 10));

        Assert.Equal(2, result.Count);
        Assert.All(result, s => Assert.Equal(dune.Id, s.MovieId));
        Assert.True(result[0].StartTimeUtc < result[1].StartTimeUtc);
    }

    [Fact]
    public async Task GetForMovieOnDate_projects_room_branch_and_city()
    {
        using var db = TestDatabase.Create();
        var room = db.SeedRoom("Lotte Nam Sài Gòn", "Phòng 3", "TP. Hồ Chí Minh");
        var movie = db.SeedMovie("Dune");
        db.SeedShowtime(movie, room, new DateTime(2026, 10, 10, 5, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var sut = Sut(db, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        var item = Assert.Single(await sut.GetForMovieOnDateAsync(movie.Id, new DateOnly(2026, 10, 10)));

        Assert.Equal("Dune", item.MovieTitle);
        Assert.Equal("Phòng 3", item.RoomName);
        Assert.Equal(room.TheaterId, item.TheaterId);
        Assert.Equal("Lotte Nam Sài Gòn", item.TheaterName);
        Assert.Equal("TP. Hồ Chí Minh", item.City);
        Assert.Equal(90_000m, item.BasePrice);
    }

    [Fact]
    public async Task GetAvailableDates_skips_showtimes_that_already_started()
    {
        using var db = TestDatabase.Create();
        var room = db.SeedRoom();
        var movie = db.SeedMovie("Dune");

        var past = new DateTime(2026, 10, 10, 2, 0, 0, DateTimeKind.Utc);
        var future = new DateTime(2026, 10, 11, 2, 0, 0, DateTimeKind.Utc);
        db.SeedShowtime(movie, room, past);
        db.SeedShowtime(movie, room, future);
        await db.SaveChangesAsync();

        var sut = Sut(db, new DateTime(2026, 10, 10, 6, 0, 0, DateTimeKind.Utc));

        var dates = await sut.GetAvailableDatesAsync(movie.Id, new DateOnly(2026, 10, 10));

        Assert.Equal(new DateOnly(2026, 10, 11), Assert.Single(dates));
    }

    [Fact]
    public async Task GetAvailableDates_returns_distinct_days_inside_the_window_only()
    {
        using var db = TestDatabase.Create();
        var room = db.SeedRoom();
        var movie = db.SeedMovie("Dune");

        // Hai suất cùng ngày ⇒ một mục; một suất ngoài cửa sổ 3 ngày ⇒ bị loại.
        db.SeedShowtime(movie, room, new DateTime(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc));
        db.SeedShowtime(movie, room, new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc));
        db.SeedShowtime(movie, room, new DateTime(2026, 10, 11, 3, 0, 0, DateTimeKind.Utc));
        db.SeedShowtime(movie, room, new DateTime(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var sut = Sut(db, new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc));

        var dates = await sut.GetAvailableDatesAsync(movie.Id, new DateOnly(2026, 10, 10), dayCount: 3);

        Assert.Equal(new[] { new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 11) }, dates);
    }

    [Fact]
    public async Task GetForMovieOnDate_filters_by_city_and_by_branch()
    {
        using var db = TestDatabase.Create();
        var hanoiA = db.SeedRoom("Vincom Bà Triệu", city: "Hà Nội");
        var hanoiB = db.SeedRoom("Aeon Long Biên", city: "Hà Nội");
        var saigon = db.SeedRoom("Aeon Tân Phú", city: "TP. Hồ Chí Minh");
        var movie = db.SeedMovie("Dune");
        var start = new DateTime(2026, 10, 10, 5, 0, 0, DateTimeKind.Utc);
        db.SeedShowtime(movie, hanoiA, start);
        db.SeedShowtime(movie, hanoiB, start.AddHours(1));
        db.SeedShowtime(movie, saigon, start.AddHours(2));
        await db.SaveChangesAsync();

        var sut = Sut(db, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        var day = new DateOnly(2026, 10, 10);

        var all = await sut.GetForMovieOnDateAsync(movie.Id, day);
        var hanoi = await sut.GetForMovieOnDateAsync(movie.Id, day, new ShowtimeQueryFilter(City: "Hà Nội"));
        var oneBranch = await sut.GetForMovieOnDateAsync(movie.Id, day, new ShowtimeQueryFilter(TheaterId: hanoiB.TheaterId));

        Assert.Equal(3, all.Count);
        Assert.Equal(new[] { "Vincom Bà Triệu", "Aeon Long Biên" }, hanoi.Select(s => s.TheaterName));
        Assert.Equal("Aeon Long Biên", Assert.Single(oneBranch).TheaterName);
    }

    [Fact]
    public async Task Queries_hide_showtimes_of_closed_branches()
    {
        using var db = TestDatabase.Create();
        var open = db.SeedRoom("Vincom Bà Triệu");
        var closed = db.SeedRoom("Aeon Long Biên");
        closed.Theater!.IsActive = false;
        var movie = db.SeedMovie("Dune");
        db.SeedShowtime(movie, open, new DateTime(2026, 10, 10, 5, 0, 0, DateTimeKind.Utc));
        db.SeedShowtime(movie, closed, new DateTime(2026, 10, 11, 5, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var sut = Sut(db, new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc));
        var from = new DateOnly(2026, 10, 10);

        Assert.Empty(await sut.GetForMovieOnDateAsync(movie.Id, new DateOnly(2026, 10, 11)));
        Assert.Equal(from, Assert.Single(await sut.GetAvailableDatesAsync(movie.Id, from)));
        Assert.Equal("Vincom Bà Triệu", Assert.Single(await sut.GetBranchesForMovieAsync(movie.Id, from)).Name);
    }

    [Fact]
    public async Task GetBranchesForMovie_lists_each_branch_once_sorted_by_city_then_name()
    {
        using var db = TestDatabase.Create();
        var saigon = db.SeedRoom("Aeon Tân Phú", city: "TP. Hồ Chí Minh");
        var hanoiB = db.SeedRoom("Vincom Bà Triệu", city: "Hà Nội");
        var hanoiA = db.SeedRoom("Aeon Long Biên", city: "Hà Nội");
        var movie = db.SeedMovie("Dune");
        var start = new DateTime(2026, 10, 10, 5, 0, 0, DateTimeKind.Utc);
        db.SeedShowtime(movie, saigon, start);
        db.SeedShowtime(movie, hanoiB, start);
        db.SeedShowtime(movie, hanoiB, start.AddHours(3));
        db.SeedShowtime(movie, hanoiA, start);
        await db.SaveChangesAsync();

        var sut = Sut(db, new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc));

        var branches = await sut.GetBranchesForMovieAsync(movie.Id, new DateOnly(2026, 10, 10));

        Assert.Equal(
            new[] { "Aeon Long Biên", "Vincom Bà Triệu", "Aeon Tân Phú" },
            branches.Select(b => b.Name));
    }
}
