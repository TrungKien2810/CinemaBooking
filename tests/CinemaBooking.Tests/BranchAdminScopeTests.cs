using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Controllers;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

/// <summary>
/// Phạm vi của tài khoản quản lý chi nhánh (BranchAdmin): chỉ phòng/ghế và suất chiếu của chi nhánh
/// mình. Đây là loại lỗi bảo mật dễ lọt nhất — sửa <c>?theaterId=</c> hay <c>RoomId</c> trên form
/// là đụng được chi nhánh khác nếu controller tin tham số.
/// </summary>
public sealed class BranchAdminScopeTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<(CinemaDbContext Db, Room Own, Room Other)> SeedAsync()
    {
        var db = TestDatabase.Create();

        var ownTheater = new Theater { Name = "CineSmart Cầu Giấy", City = "Hà Nội" };
        var otherTheater = new Theater { Name = "CineSmart Nguyễn Huệ", City = "TP. Hồ Chí Minh" };
        var own = new Room { Theater = ownTheater, RoomName = "Phòng 1", TotalRows = 3, TotalColumns = 3 };
        var other = new Room { Theater = otherTheater, RoomName = "Phòng 1", TotalRows = 3, TotalColumns = 3 };

        db.Theaters.AddRange(ownTheater, otherTheater);
        db.Rooms.AddRange(own, other);
        await db.SaveChangesAsync();

        db.Seats.AddRange(SeatGenerator.Generate(own));
        db.Seats.AddRange(SeatGenerator.Generate(other));
        await db.SaveChangesAsync();

        return (db, own, other);
    }

    [Fact]
    public async Task Branch_admin_can_open_own_room_but_not_another_theaters_room()
    {
        var (db, own, other) = await SeedAsync();
        var controller = new RoomsController(db, FakeBranchScope.Branch(own.TheaterId));

        Assert.IsType<ViewResult>(await controller.Edit(own.Id, CancellationToken.None));
        Assert.IsType<ForbidResult>(await controller.Edit(other.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Branch_admin_cannot_open_another_theaters_seat_editor()
    {
        var (db, own, other) = await SeedAsync();
        var controller = new RoomsController(db, FakeBranchScope.Branch(own.TheaterId));

        Assert.IsType<ViewResult>(await controller.Seats(own.Id, CancellationToken.None));
        Assert.IsType<ForbidResult>(await controller.Seats(other.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Branch_admin_cannot_list_another_theaters_rooms_by_editing_the_url()
    {
        var (db, own, other) = await SeedAsync();
        var controller = new RoomsController(db, FakeBranchScope.Branch(own.TheaterId));

        Assert.IsType<ViewResult>(await controller.Index(own.TheaterId, CancellationToken.None));
        Assert.IsType<ForbidResult>(await controller.Index(other.TheaterId, CancellationToken.None));
    }

    [Fact]
    public async Task Branch_admin_cannot_create_a_room_in_another_theater()
    {
        var (db, own, other) = await SeedAsync();
        var controller = new RoomsController(db, FakeBranchScope.Branch(own.TheaterId));

        var result = await controller.Create(
            new RoomFormModel { TheaterId = other.TheaterId, RoomName = "Lậu", TotalRows = 1, TotalColumns = 1 },
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Seat_editor_saves_vip_but_last_row_stays_couple()
    {
        var (db, own, _) = await SeedAsync();
        var controller = new RoomsController(db, FakeBranchScope.Branch(own.TheaterId));

        var seats = await db.Seats.Where(s => s.RoomId == own.Id).ToListAsync();
        var a1 = seats.Single(s => s.Label == "A1");
        var c1 = seats.Single(s => s.Label == "C1"); // hàng cuối (3) → ghế đôi

        var result = await controller.Seats(own.Id, $"{a1.Id}=Vip;{c1.Id}=Standard", CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);

        db.ChangeTracker.Clear();
        Assert.Equal(SeatType.Vip, (await db.Seats.SingleAsync(s => s.Id == a1.Id)).SeatType);
        Assert.Equal(SeatType.Couple, (await db.Seats.SingleAsync(s => s.Id == c1.Id)).SeatType);
    }

    [Fact]
    public async Task Seat_editor_never_turns_a_non_last_row_into_a_couple_seat()
    {
        var (db, own, _) = await SeedAsync();
        var controller = new RoomsController(db, FakeBranchScope.Branch(own.TheaterId));

        var b1 = await db.Seats.SingleAsync(s => s.RoomId == own.Id && s.RowNumber == 2 && s.ColumnNumber == 1);
        var before = b1.SeatType;

        await controller.Seats(own.Id, $"{b1.Id}=Couple", CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(before, (await db.Seats.SingleAsync(s => s.Id == b1.Id)).SeatType);
    }

    [Fact]
    public async Task Branch_admin_cannot_schedule_a_showtime_in_another_theaters_room()
    {
        var (db, own, other) = await SeedAsync();
        var movie = db.SeedMovie("Mắt Biếc");
        await db.SaveChangesAsync();

        var controller = new ShowtimesController(
            db, new ShowtimeScheduler(db), Options.Create(new DisplayTimeZone()),
            new FixedTimeProvider(Now), FakeBranchScope.Branch(own.TheaterId));

        var form = new ShowtimeFormModel
        {
            MovieId = movie.Id,
            RoomId = other.Id,
            StartTimeLocal = new DateTime(2026, 10, 3, 10, 0, 0),
            BasePrice = 90_000m,
        };

        var result = await controller.Create(form, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await db.Showtimes.ToListAsync());
    }

    [Fact]
    public async Task Branch_admin_only_sees_own_theater_in_the_showtime_filter()
    {
        var (db, own, _) = await SeedAsync();
        var controller = new ShowtimesController(
            db, new ShowtimeScheduler(db), Options.Create(new DisplayTimeZone()),
            new FixedTimeProvider(Now), FakeBranchScope.Branch(own.TheaterId));

        var result = await controller.Index(null, new DateOnly(2026, 10, 3), CancellationToken.None);

        var model = Assert.IsType<ShowtimesIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        var only = Assert.Single(model.Theaters);
        Assert.Equal(own.TheaterId.ToString(), only.Value);
    }
}
