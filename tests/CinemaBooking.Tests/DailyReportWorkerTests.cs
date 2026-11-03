using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Worker.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CinemaBooking.Tests;

/// <summary>
/// Báo cáo ngày là dữ liệu tài chính: sai chi nhánh (hoặc không ghi được vì thiếu TheaterId) và sai
/// ngày (UTC thay vì giờ rạp) đều là lỗi im lặng — worker chỉ log rồi thử lại mỗi giờ. Test khóa cả
/// hai điều kiện đó lại.
/// </summary>
public sealed class DailyReportWorkerTests : IAsyncLifetime
{
    // 20:00 UTC ngày 15 = 03:00 ngày 16 giờ Việt Nam: đơn rơi sang NGÀY KHÁC nếu gom theo UTC.
    private static readonly DateTime BookingUtc = new(2026, 10, 15, 20, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NowUtc = new(2026, 10, 17, 2, 0, 0, DateTimeKind.Utc);

    private readonly string _dbName = $"report-{Guid.NewGuid()}";
    private ServiceProvider _provider = null!;

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CinemaDbContext>(o => o.UseInMemoryDatabase(_dbName));
        _provider = services.BuildServiceProvider();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Groups_by_theater_and_movie_on_the_local_calendar_day()
    {
        var (theaterA, movie) = await SeedAsync();
        var theaterB = await SeedTheaterAsync("CGV Times City", price: 120_000m);

        await Worker(offsetHours: 7).AggregateMissingDaysAsync(default);

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();

        var reports = await db.DailyRevenueReports.OrderBy(r => r.TheaterId).ToListAsync();

        // Hai chi nhánh = hai dòng, không gộp chung doanh thu.
        Assert.Equal(2, reports.Count);
        Assert.All(reports, r => Assert.Equal(new DateOnly(2026, 10, 16), r.ReportDate));
        Assert.All(reports, r => Assert.Equal(movie.Id, r.MovieId));
        Assert.Equal(new[] { theaterA.Id, theaterB.Id }, reports.Select(r => r.TheaterId).ToArray());

        Assert.Equal(2, reports[0].TicketsSold);
        Assert.Equal(180_000m, reports[0].Revenue);
        Assert.Equal(2, reports[1].TicketsSold);
        Assert.Equal(240_000m, reports[1].Revenue);
    }

    [Fact]
    public async Task Running_twice_does_not_duplicate_the_same_day()
    {
        await SeedAsync();

        await Worker(offsetHours: 7).AggregateMissingDaysAsync(default);
        await Worker(offsetHours: 7).AggregateMissingDaysAsync(default);

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();
        Assert.Equal(1, await db.DailyRevenueReports.CountAsync());
    }

    [Fact]
    public async Task Skips_the_current_local_day_because_it_is_not_finished_yet()
    {
        // Đơn đặt hôm nay (giờ rạp) chưa được chốt sổ: báo cáo chỉ dựng cho ngày đã qua.
        await SeedAsync(bookingUtc: NowUtc.AddHours(-1));

        await Worker(offsetHours: 7).AggregateMissingDaysAsync(default);

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();
        Assert.Empty(await db.DailyRevenueReports.ToListAsync());
    }

    private DailyReportWorker Worker(int offsetHours) =>
        new(_provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(NowUtc),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Display:TimeZoneOffsetHours"] = offsetHours.ToString(),
                })
                .Build(),
            NullLogger<DailyReportWorker>.Instance);

    private async Task<(Theater Theater, Movie Movie)> SeedAsync(DateTime? bookingUtc = null)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();

        var theater = new Theater { Name = "CineSmart Hà Nội", City = "Hà Nội" };
        var room = new Room { Theater = theater, RoomName = "Phòng 1", TotalRows = 2, TotalColumns = 2 };
        var movie = new Movie { Title = "Mắt Biếc", DurationMinutes = 100 };
        var start = bookingUtc ?? BookingUtc;
        var showtime = new Showtime
        {
            Movie = movie,
            Room = room,
            StartTimeUtc = start,
            EndTimeUtc = start.AddMinutes(100),
            BasePrice = 90_000m,
        };

        var seats = new[]
        {
            new Seat { Room = room, RowNumber = 1, ColumnNumber = 1, SeatType = SeatType.Standard },
            new Seat { Room = room, RowNumber = 1, ColumnNumber = 2, SeatType = SeatType.Standard },
        };

        db.Bookings.Add(new Booking
        {
            BookingCode = $"CB-{Guid.NewGuid():N}"[..12],
            Showtime = showtime,
            CustomerName = "Nguyễn Văn A",
            CustomerEmail = "a@example.com",
            BookingTimeUtc = start,
            TotalAmount = 180_000m,
            Status = BookingStatus.Confirmed,
            Seats =
            {
                new BookingSeat { Seat = seats[0], Showtime = showtime, PriceAtBooking = 90_000m },
                new BookingSeat { Seat = seats[1], Showtime = showtime, PriceAtBooking = 90_000m },
            },
        });

        await db.SaveChangesAsync();
        return (theater, movie);
    }

    private async Task<Theater> SeedTheaterAsync(string name, decimal price)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();

        var theater = new Theater { Name = name, City = "Hà Nội" };
        var room = new Room { Theater = theater, RoomName = "Phòng 1", TotalRows = 2, TotalColumns = 2 };

        // Cùng phim với rạp kia để chứng minh khóa gom nhóm là (chi nhánh, phim).
        var movie = await db.Movies.SingleAsync();
        var showtime = new Showtime
        {
            Movie = movie,
            Room = room,
            StartTimeUtc = BookingUtc,
            EndTimeUtc = BookingUtc.AddMinutes(100),
            BasePrice = price,
        };

        var seats = new[]
        {
            new Seat { Room = room, RowNumber = 1, ColumnNumber = 1, SeatType = SeatType.Standard },
            new Seat { Room = room, RowNumber = 1, ColumnNumber = 2, SeatType = SeatType.Standard },
        };

        db.Bookings.Add(new Booking
        {
            BookingCode = $"CB-{Guid.NewGuid():N}"[..12],
            Showtime = showtime,
            CustomerName = "Trần Thị B",
            CustomerEmail = "b@example.com",
            BookingTimeUtc = BookingUtc,
            TotalAmount = price * 2,
            Status = BookingStatus.Confirmed,
            Seats =
            {
                new BookingSeat { Seat = seats[0], Showtime = showtime, PriceAtBooking = price },
                new BookingSeat { Seat = seats[1], Showtime = showtime, PriceAtBooking = price },
            },
        });

        await db.SaveChangesAsync();
        return theater;
    }
}
