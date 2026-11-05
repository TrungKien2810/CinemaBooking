using CinemaBooking.Domain.Entities;
using CinemaBooking.Web.Areas.Admin.Controllers;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

public class BranchDetailsTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public async Task Details_lists_only_bookable_movies_rooms_and_running_promotions_of_that_branch()
    {
        using var db = TestDatabase.Create();
        var room = db.SeedRoom("CineSmart A");
        var other = db.SeedRoom("CineSmart B");
        var shown = db.SeedMovie("Phim hiện");
        var hiddenShowtimeMovie = db.SeedMovie("Phim bị ẩn suất");
        var otherBranchMovie = db.SeedMovie("Phim chi nhánh khác");
        db.SeedShowtime(shown, room, Now.AddHours(5));
        db.SeedShowtime(hiddenShowtimeMovie, room, Now.AddHours(6)).IsActive = false;
        db.SeedShowtime(otherBranchMovie, other, Now.AddHours(5));
        await db.SaveChangesAsync();
        db.Promotions.AddRange(
            new Promotion { TheaterId = room.TheaterId, Title = "Đang chạy", StartDate = Today, EndDate = Today.AddDays(5) },
            new Promotion { TheaterId = room.TheaterId, Title = "Hết hạn", StartDate = Today.AddDays(-9), EndDate = Today.AddDays(-1) },
            new Promotion { TheaterId = room.TheaterId, Title = "Tắt", StartDate = Today, EndDate = Today.AddDays(5), IsActive = false },
            new Promotion { TheaterId = other.TheaterId, Title = "Của chi nhánh khác", StartDate = Today, EndDate = Today.AddDays(5) });
        await db.SaveChangesAsync();

        var details = await new BranchDirectory(db).GetDetailsAsync(room.TheaterId, Now, Today);

        Assert.NotNull(details);
        Assert.Equal(["Phim hiện"], details.Movies.Select(m => m.Title));
        Assert.Equal(["Đang chạy"], details.Promotions.Select(p => p.Title));
        Assert.Equal(["Phòng 1"], details.Rooms.Select(r => r.Name));
    }

    [Fact]
    public async Task Details_is_null_for_a_closed_branch()
    {
        using var db = TestDatabase.Create();
        var room = db.SeedRoom();
        room.Theater!.IsActive = false;
        await db.SaveChangesAsync();

        Assert.Null(await new BranchDirectory(db).GetDetailsAsync(room.TheaterId, Now, Today));
    }

    private static PromotionsController Controller(CinemaBooking.Infrastructure.Persistence.CinemaDbContext db, FakeBranchScope scope) =>
        new(db, scope, Options.Create(new DisplayTimeZone()), new FixedTimeProvider(Now));

    [Fact]
    public async Task Branch_admin_cannot_create_or_delete_promotions_of_another_branch()
    {
        using var db = TestDatabase.Create();
        var own = db.SeedRoom("CineSmart A");
        var other = db.SeedRoom("CineSmart B");
        await db.SaveChangesAsync();
        var foreign = new Promotion { TheaterId = other.TheaterId, Title = "X", StartDate = Today, EndDate = Today };
        db.Promotions.Add(foreign);
        await db.SaveChangesAsync();
        var controller = Controller(db, FakeBranchScope.Branch(own.TheaterId));

        var create = await controller.Create(
            new PromotionFormModel { TheaterId = other.TheaterId, Title = "Lậu", StartDate = Today, EndDate = Today }, CancellationToken.None);
        var delete = await controller.Delete(foreign.Id, CancellationToken.None);

        Assert.IsType<ForbidResult>(create);
        Assert.IsType<ForbidResult>(delete);
        Assert.Equal(1, await db.Promotions.CountAsync());
    }

    [Fact]
    public async Task Promotion_end_date_before_start_date_is_invalid()
    {
        var form = new PromotionFormModel { Title = "X", StartDate = Today, EndDate = Today.AddDays(-1) };
        var errors = form.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(form)).ToList();

        Assert.Single(errors);
        await Task.CompletedTask;
    }
}
