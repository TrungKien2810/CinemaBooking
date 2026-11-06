using System.Security.Claims;
using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Tests;

public class BranchScopeFailClosedTests
{
    private static (BranchScope Scope, CinemaDbContext Db) Build(string role, int? userTheaterId)
    {
        var db = TestDatabase.Create();
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1", TheaterId = userTheaterId });
        db.SaveChanges();

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "u1"), new Claim(ClaimTypes.Role, role)], "test");
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return (new BranchScope(http, db), db);
    }

    [Fact]
    public async Task Admin_is_unscoped()
    {
        var (scope, _) = Build(AppRoles.Admin, null);

        Assert.Null(await scope.TheaterIdAsync());
        Assert.True(await scope.CanAccessTheaterAsync(5));
    }

    [Fact]
    public async Task Branch_admin_is_scoped_to_the_assigned_theater()
    {
        var (scope, _) = Build(AppRoles.BranchAdmin, 3);

        Assert.Equal(3, await scope.TheaterIdAsync());
        Assert.True(await scope.CanAccessTheaterAsync(3));
        Assert.False(await scope.CanAccessTheaterAsync(4));
    }

    [Fact]
    public async Task Branch_admin_without_a_theater_is_NOT_treated_as_a_system_admin()
    {
        var (scope, _) = Build(AppRoles.BranchAdmin, null);

        // Trước đây trả null (= không giới hạn); giờ phải là một id không tồn tại và từ chối mọi chi nhánh.
        Assert.Equal(BranchScope.NoBranch, await scope.TheaterIdAsync());
        Assert.False(await scope.CanAccessTheaterAsync(1));
        Assert.False(await scope.CanAccessTheaterAsync(BranchScope.NoBranch + 1));
    }
}

public class CatalogDetailsTests
{
    [Fact]
    public async Task Seeding_fills_movie_details_room_types_and_subtitles_and_is_idempotent()
    {
        using var db = TestDatabase.Create();

        await CatalogSeeder.SeedAsync(db);
        await CatalogSeeder.SeedAsync(db);

        var dune = await db.Movies.SingleAsync(m => m.Title == "Dune: Part Two");
        Assert.Equal("Denis Villeneuve", dune.Director);
        Assert.Equal(AgeRating.T13, dune.AgeRating);

        var imax = await db.Rooms.Include(r => r.Theater)
            .SingleAsync(r => r.Theater!.Name == "CineSmart Cầu Giấy" && r.RoomName == "Phòng A");
        Assert.Equal(RoomType.Imax, imax.RoomType);

        // Phim nước ngoài chiếu phụ đề, phim Việt giữ bản gốc.
        Assert.All(await db.Showtimes.Where(s => s.Movie!.Title == "Dune: Part Two").ToListAsync(),
            s => Assert.Equal(ShowLanguage.Subtitled, s.Language));
        Assert.All(await db.Showtimes.Where(s => s.Movie!.Title == "Mắt Biếc").ToListAsync(),
            s => Assert.Equal(ShowLanguage.Original, s.Language));
    }

    [Fact]
    public async Task Upgrade_does_not_overwrite_details_an_admin_already_entered()
    {
        using var db = TestDatabase.Create();
        await CatalogSeeder.SeedAsync(db);

        var movie = await db.Movies.SingleAsync(m => m.Title == "Inside Out 2");
        movie.Director = "Người khác";
        movie.AgeRating = AgeRating.T18;
        await db.SaveChangesAsync();

        await CatalogSeeder.SeedAsync(db);

        db.ChangeTracker.Clear();
        var again = await db.Movies.SingleAsync(m => m.Title == "Inside Out 2");
        Assert.Equal("Người khác", again.Director);
        Assert.Equal(AgeRating.T18, again.AgeRating);
    }

    [Theory]
    [InlineData("https://youtu.be/abc", true)]
    [InlineData("http://example.com/t", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,<script>", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void Trailer_link_only_accepts_http_and_https(string? url, bool safe)
    {
        var extra = new MovieExtraInfo(AgeRating.P, null, null, url);

        Assert.Equal(safe, extra.SafeTrailerUrl is not null);
    }

    [Fact]
    public void Labels_are_in_vietnamese_and_cover_every_value()
    {
        foreach (var t in Enum.GetValues<RoomType>()) Assert.False(string.IsNullOrWhiteSpace(t.Label()));
        foreach (var l in Enum.GetValues<ShowLanguage>()) Assert.False(string.IsNullOrWhiteSpace(l.Label()));
        foreach (var r in Enum.GetValues<AgeRating>()) Assert.StartsWith(r.Label(), r.Describe());

        Assert.Equal("Phụ đề", ShowLanguage.Subtitled.Label());
        Assert.Equal("IMAX", RoomType.Imax.Label());
    }
}

public class PromotionSeedGateTests
{
    [Fact]
    public async Task Restarting_does_not_switch_back_on_a_discount_the_branch_owner_turned_off()
    {
        using var db = TestDatabase.Create();
        await CatalogSeeder.SeedAsync(db);

        // Chủ chi nhánh đổi "Thứ 3 đồng giá" thành chỉ quảng bá nhưng giữ nguyên mô tả.
        var promo = await db.Promotions.SingleAsync(p => p.Title == "Thứ 3 đồng giá");
        promo.FixedPrice = null;
        promo.ApplicableSeatType = null;
        promo.DaysOfWeekMask = 0;
        await db.SaveChangesAsync();

        await CatalogSeeder.SeedAsync(db);

        db.ChangeTracker.Clear();
        var again = await db.Promotions.SingleAsync(p => p.Title == "Thứ 3 đồng giá");
        Assert.Null(again.FixedPrice);
        Assert.False(again.AppliesAutomatically);
    }

    [Fact]
    public async Task A_database_from_before_discount_rules_gets_them_once()
    {
        using var db = TestDatabase.Create();
        await CatalogSeeder.SeedAsync(db);
        foreach (var p in db.Promotions)
        {
            p.DiscountPercent = null; p.FixedPrice = null; p.ApplicableSeatType = null;
            p.DaysOfWeekMask = 0; p.FromTime = null; p.ToTime = null;
        }
        await db.SaveChangesAsync();

        await CatalogSeeder.SeedAsync(db);

        db.ChangeTracker.Clear();
        Assert.Equal(60_000m, (await db.Promotions.SingleAsync(p => p.Title == "Thứ 3 đồng giá")).FixedPrice);
        Assert.Equal(25, (await db.Promotions.SingleAsync(p => p.Title == "Suất sớm")).DiscountPercent);
        Assert.False((await db.Promotions.SingleAsync(p => p.Title == "Sinh viên")).AppliesAutomatically);
    }
}
