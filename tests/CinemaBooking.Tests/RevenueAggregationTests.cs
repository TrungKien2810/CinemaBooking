using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Controllers;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

/// <summary>
/// Thống kê chỉ tính đơn Confirmed, gom theo ngày ĐẶT ở giờ rạp (UTC+7) và theo chi nhánh / phim / suất.
/// </summary>
public class RevenueAggregationTests
{
    /// <summary>12:00 ngày 05/10 giờ rạp → khoảng mặc định 29/09 – 05/10.</summary>
    private static readonly DateTime Now = new(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc);

    private sealed record Fixture(CinemaDbContext Db, Theater North, Theater South, Movie MovieA, Movie MovieB, Showtime NorthShow, Showtime SouthShow);

    private static async Task<Fixture> SeedAsync()
    {
        var db = TestDatabase.Create();
        var north = new Theater { Name = "CineSmart Cầu Giấy", City = "Hà Nội" };
        var south = new Theater { Name = "CineSmart Quận 1", City = "TP.HCM" };
        var northRoom = new Room { Theater = north, RoomName = "Phòng 1", TotalRows = 2, TotalColumns = 3 };
        var southRoom = new Room { Theater = south, RoomName = "Phòng 1", TotalRows = 2, TotalColumns = 2 };
        db.Rooms.AddRange(northRoom, southRoom);
        var movieA = db.SeedMovie("Mắt Biếc");
        var movieB = db.SeedMovie("Lật Mặt");
        await db.SaveChangesAsync();

        db.Seats.AddRange(SeatGenerator.Generate(northRoom));
        db.Seats.AddRange(SeatGenerator.Generate(southRoom));
        var northShow = db.SeedShowtime(movieA, northRoom, new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc));
        var southShow = db.SeedShowtime(movieB, southRoom, new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var f = new Fixture(db, north, south, movieA, movieB, northShow, southShow);

        // 16:30 UTC 01/10 = 23:30 giờ rạp 01/10.
        Book(f, northShow, BookingStatus.Confirmed, new DateTime(2026, 10, 1, 16, 30, 0, DateTimeKind.Utc), 100_000m, 100_000m);
        // 17:30 UTC 01/10 = 00:30 giờ rạp 02/10 — đã sang ngày mới dù UTC vẫn là 01/10.
        Book(f, northShow, BookingStatus.Confirmed, new DateTime(2026, 10, 1, 17, 30, 0, DateTimeKind.Utc), 120_000m);
        Book(f, southShow, BookingStatus.Confirmed, new DateTime(2026, 10, 2, 3, 0, 0, DateTimeKind.Utc), 90_000m);
        // Không được tính: đơn chưa xác nhận / hết hạn, và đơn ngoài khoảng ngày.
        Book(f, southShow, BookingStatus.Pending, new DateTime(2026, 10, 2, 4, 0, 0, DateTimeKind.Utc), 90_000m);
        Book(f, northShow, BookingStatus.Expired, new DateTime(2026, 10, 2, 4, 0, 0, DateTimeKind.Utc), 100_000m);
        Book(f, northShow, BookingStatus.Confirmed, new DateTime(2026, 9, 20, 3, 0, 0, DateTimeKind.Utc), 100_000m);
        await db.SaveChangesAsync();

        return f;
    }

    private static int _code;

    private static void Book(Fixture f, Showtime showtime, BookingStatus status, DateTime bookedUtc, params decimal[] prices)
    {
        var booking = new Booking
        {
            BookingCode = $"CB-R{Interlocked.Increment(ref _code):00000}",
            Showtime = showtime,
            CustomerName = "Khách",
            CustomerEmail = "khach@example.com",
            Status = status,
            BookingTimeUtc = bookedUtc,
            TotalAmount = prices.Sum(),
        };
        // InMemory không kiểm unique index — SeatId chỉ cần khác nhau trong cùng đơn.
        for (var i = 0; i < prices.Length; i++)
            booking.Seats.Add(new BookingSeat { SeatId = 1000 + i, Showtime = showtime, PriceAtBooking = prices[i] });
        f.Db.Bookings.Add(booking);
    }

    private static RevenueController CreateController(CinemaDbContext db) =>
        new(db, Options.Create(new DisplayTimeZone()), new FixedTimeProvider(Now));

    [Fact]
    public async Task Dashboard_groups_confirmed_tickets_by_local_day_and_branch()
    {
        var f = await SeedAsync();

        var result = await CreateController(f.Db).Index(null, null, CancellationToken.None);

        var model = Assert.IsType<RevenueDashboardViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(new DateOnly(2026, 9, 29), model.From);
        Assert.Equal(new DateOnly(2026, 10, 5), model.To);
        Assert.Equal(
            [
                new RevenueByDayRow(new DateOnly(2026, 10, 2), f.North.Id, f.North.Name, 1, 120_000m),
                new RevenueByDayRow(new DateOnly(2026, 10, 2), f.South.Id, f.South.Name, 1, 90_000m),
                new RevenueByDayRow(new DateOnly(2026, 10, 1), f.North.Id, f.North.Name, 2, 200_000m),
            ],
            model.ByDay);
        Assert.Equal(
            [
                new RevenueByTheaterRow(f.North.Id, f.North.Name, 3, 320_000m),
                new RevenueByTheaterRow(f.South.Id, f.South.Name, 1, 90_000m),
            ],
            model.ByTheater);
        Assert.Equal(4, model.TotalTickets);
        Assert.Equal(410_000m, model.TotalRevenue);
    }

    [Fact]
    public async Task Dashboard_range_uses_local_day_boundaries()
    {
        var f = await SeedAsync();

        // Chỉ ngày 02/10 giờ rạp: lấy đơn 17:30 UTC 01/10, bỏ đơn 16:30 UTC 01/10.
        var day = new DateOnly(2026, 10, 2);
        var result = await CreateController(f.Db).Index(day, day, CancellationToken.None);

        var model = Assert.IsType<RevenueDashboardViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(2, model.TotalTickets);
        Assert.Equal(210_000m, model.TotalRevenue);
    }

    [Fact]
    public async Task Breakdown_splits_by_movie_and_showtime_with_occupancy()
    {
        var f = await SeedAsync();

        var result = await CreateController(f.Db).Breakdown(null, null, null, CancellationToken.None);

        var model = Assert.IsType<RevenueBreakdownViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(
            [
                new RevenueByMovieRow(f.MovieA.Id, "Mắt Biếc", 3, 320_000m),
                new RevenueByMovieRow(f.MovieB.Id, "Lật Mặt", 1, 90_000m),
            ],
            model.ByMovie);

        var north = Assert.Single(model.ByShowtime, s => s.ShowtimeId == f.NorthShow.Id);
        Assert.Equal(3, north.Tickets);
        Assert.Equal(6, north.Capacity);
        Assert.Equal(0.5, north.Occupancy);
        Assert.Equal(new DateTime(2026, 10, 3, 19, 0, 0), north.StartLocal);
    }

    [Fact]
    public async Task Breakdown_filters_by_branch()
    {
        var f = await SeedAsync();

        var result = await CreateController(f.Db).Breakdown(null, null, f.South.Id, CancellationToken.None);

        var model = Assert.IsType<RevenueBreakdownViewModel>(Assert.IsType<ViewResult>(result).Model);
        var movie = Assert.Single(model.ByMovie);
        Assert.Equal(f.MovieB.Id, movie.MovieId);
        var showtime = Assert.Single(model.ByShowtime);
        Assert.Equal(f.SouthShow.Id, showtime.ShowtimeId);
    }
}
