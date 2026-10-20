using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Tests;

/// <summary>
/// Provider InMemory cho test lọc/sắp xếp. Nó KHÔNG kiểm unique index và không dịch SQL,
/// nên mọi ràng buộc ở tầng DB (ví dụ unique (ShowtimeId, SeatId)) phải test bằng DB thật.
/// </summary>
internal static class TestDatabase
{
    public static CinemaDbContext Create()
    {
        var options = new DbContextOptionsBuilder<CinemaDbContext>()
            .UseInMemoryDatabase($"cinema-{Guid.NewGuid()}")
            .Options;

        return new CinemaDbContext(options);
    }

    /// <summary>Một rạp, một phòng — đủ cho các truy vấn catalog không cần sơ đồ ghế.</summary>
    public static Room SeedRoom(
        this CinemaDbContext db,
        string theaterName = "CGV Vincom",
        string roomName = "Phòng 1",
        string city = "Hà Nội")
    {
        var theater = new Theater { Name = theaterName, City = city };
        var room = new Room { Theater = theater, RoomName = roomName, TotalRows = 5, TotalColumns = 8 };
        db.Theaters.Add(theater);
        db.Rooms.Add(room);
        return room;
    }

    public static Movie SeedMovie(
        this CinemaDbContext db,
        string title,
        string? genre = "Hành động",
        int durationMinutes = 120,
        bool isActive = true,
        DateOnly releaseDate = default)
    {
        var movie = new Movie
        {
            Title = title,
            Genre = genre,
            DurationMinutes = durationMinutes,
            IsActive = isActive,
            ReleaseDate = releaseDate == default ? new DateOnly(2026, 1, 1) : releaseDate,
        };
        db.Movies.Add(movie);
        return movie;
    }

    public static Showtime SeedShowtime(this CinemaDbContext db, Movie movie, Room room, DateTime startUtc)
    {
        var showtime = new Showtime
        {
            Movie = movie,
            Room = room,
            StartTimeUtc = startUtc,
            EndTimeUtc = startUtc.AddMinutes(movie.DurationMinutes),
            BasePrice = 90_000m,
        };
        db.Showtimes.Add(showtime);
        return showtime;
    }
}

/// <summary>Đồng hồ đứng yên, để test "suất đã bắt đầu thì không hiện" không phụ thuộc giờ chạy test.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTime utcNow) => _now = new DateTimeOffset(utcNow, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;
}
