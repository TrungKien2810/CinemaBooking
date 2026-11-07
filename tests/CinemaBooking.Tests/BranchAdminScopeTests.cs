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

    private static async Task<Dictionary<string, Seat>> SeatsByLabelAsync(CinemaDbContext db, int roomId) =>
        (await db.Seats.AsNoTracking().Where(s => s.RoomId == roomId).ToListAsync()).ToDictionary(s => s.Label);

    private static async Task<SeatType> TypeOfAsync(CinemaDbContext db, int seatId)
    {
        db.ChangeTracker.Clear();
        return (await db.Seats.SingleAsync(s => s.Id == seatId)).SeatType;
    }

    [Fact]
    public async Task Seat_editor_saves_vip_and_can_split_the_default_last_row_couple()
    {
        var (db, own, _) = await SeedAsync();
        var controller = new RoomsController(db, FakeBranchScope.Branch(own.TheaterId));
        var seats = await SeatsByLabelAsync(db, own.Id);

        // Phòng 3x3: C1–C2 là cặp mặc định, C3 là ghế thường (số cột lẻ).
        Assert.Equal(SeatType.Couple, seats["C1"].SeatType);
        Assert.Equal(SeatType.Standard, seats["C3"].SeatType);

        var result = await controller.Seats(
            own.Id, $"{seats["A1"].Id}=Vip;{seats["C1"].Id}=Standard;{seats["C2"].Id}=Standard", CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(SeatType.Vip, await TypeOfAsync(db, seats["A1"].Id));
        Assert.Equal(SeatType.Standard, await TypeOfAsync(db, seats["C1"].Id));
        Assert.Equal(SeatType.Standard, await TypeOfAsync(db, seats["C2"].Id));
    }

    [Fact]
    public async Task Seat_editor_can_make_a_couple_pair_in_any_row()
    {
        var (db, own, _) = await SeedAsync();
        var controller = new RoomsController(db, FakeBranchScope.Branch(own.TheaterId));
        var seats = await SeatsByLabelAsync(db, own.Id);

        // Hàng A (không phải hàng cuối): ghép A2 + A3.
        await controller.Seats(own.Id, $"{seats["A2"].Id}=Couple;{seats["A3"].Id}=Couple", CancellationToken.None);

        Assert.Equal(SeatType.Couple, await TypeOfAsync(db, seats["A2"].Id));
        Assert.Equal(SeatType.Couple, await TypeOfAsync(db, seats["A3"].Id));
        Assert.Equal(SeatType.Standard, await TypeOfAsync(db, seats["A1"].Id));
    }

    [Fact]
    public async Task Seat_editor_rejects_a_lone_couple_seat_and_saves_nothing()
    {
        var (db, own, _) = await SeedAsync();
        var controller = new RoomsController(db, FakeBranchScope.Branch(own.TheaterId));
        var seats = await SeatsByLabelAsync(db, own.Id);

        // A1 đơn lẻ làm ghế đôi + A3 đổi VIP: cả lần lưu bị từ chối, A3 cũng không được lưu.
        await controller.Seats(own.Id, $"{seats["A1"].Id}=Couple;{seats["A3"].Id}=Vip", CancellationToken.None);

        Assert.Equal(SeatType.Standard, await TypeOfAsync(db, seats["A1"].Id));
        Assert.Equal(SeatType.Standard, await TypeOfAsync(db, seats["A3"].Id));
    }

    [Fact]
    public async Task Seat_editor_rejects_non_adjacent_couple_seats()
    {
        var (db, own, _) = await SeedAsync();
        var controller = new RoomsController(db, FakeBranchScope.Branch(own.TheaterId));
        var seats = await SeatsByLabelAsync(db, own.Id);

        await controller.Seats(own.Id, $"{seats["A1"].Id}=Couple;{seats["A3"].Id}=Couple", CancellationToken.None);

        Assert.Equal(SeatType.Standard, await TypeOfAsync(db, seats["A1"].Id));
        Assert.Equal(SeatType.Standard, await TypeOfAsync(db, seats["A3"].Id));
    }

    [Fact]
    public async Task Seat_editor_ignores_undefined_type_values_and_seats_of_other_rooms()
    {
        var (db, own, other) = await SeedAsync();
        var controller = new RoomsController(db, FakeBranchScope.Branch(own.TheaterId));
        var seats = await SeatsByLabelAsync(db, own.Id);
        var otherSeat = await db.Seats.AsNoTracking().FirstAsync(s => s.RoomId == other.Id && s.RowNumber == 1);

        await controller.Seats(own.Id, $"{seats["A1"].Id}=7;{otherSeat.Id}=Vip", CancellationToken.None);

        Assert.Equal(SeatType.Standard, await TypeOfAsync(db, seats["A1"].Id));
        Assert.Equal(SeatType.Standard, await TypeOfAsync(db, otherSeat.Id));
    }

    [Fact]
    public async Task Seat_editor_lets_a_booked_seat_switch_between_standard_and_vip_but_not_couple()
    {
        var (db, own, _) = await SeedAsync();
        var movie = db.SeedMovie("Mắt Biếc");
        await db.SaveChangesAsync();
        var seats = await SeatsByLabelAsync(db, own.Id);

        var showtime = new Showtime
        {
            MovieId = movie.Id, RoomId = own.Id, BasePrice = 100_000m,
            StartTimeUtc = DateTime.UtcNow.AddDays(1), EndTimeUtc = DateTime.UtcNow.AddDays(1).AddHours(2),
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();
        db.Bookings.Add(new Booking
        {
            BookingCode = "CB-LOCK01", ShowtimeId = showtime.Id, CustomerName = "A", CustomerEmail = "a@b.c",
            Status = BookingStatus.Confirmed,
            Seats = { new BookingSeat { SeatId = seats["A1"].Id, ShowtimeId = showtime.Id, PriceAtBooking = 100_000m } },
        });
        await db.SaveChangesAsync();
        var controller = new RoomsController(db, FakeBranchScope.Branch(own.TheaterId));

        // Ghép ghế đôi lên ghế đang có vé: bị từ chối cả lần lưu (A3 cũng không đổi).
        await controller.Seats(own.Id, $"{seats["A1"].Id}=Couple;{seats["A2"].Id}=Couple;{seats["A3"].Id}=Vip", CancellationToken.None);
        Assert.Equal(SeatType.Standard, await TypeOfAsync(db, seats["A1"].Id));
        Assert.Equal(SeatType.Standard, await TypeOfAsync(db, seats["A3"].Id));

        // Thường → VIP cho ghế đang có vé thì được (vé đã bán giữ nguyên giá).
        await controller.Seats(own.Id, $"{seats["A1"].Id}=Vip;{seats["A2"].Id}=Vip", CancellationToken.None);
        Assert.Equal(SeatType.Vip, await TypeOfAsync(db, seats["A1"].Id));
        Assert.Equal(SeatType.Vip, await TypeOfAsync(db, seats["A2"].Id));
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
