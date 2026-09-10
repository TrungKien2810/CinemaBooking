using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Tests;

public class SeatGeneratorTests
{
    [Fact]
    public void Generate_fills_every_row_and_column()
    {
        var room = new Room { Id = 1, TotalRows = 8, TotalColumns = 12 };

        var seats = SeatGenerator.Generate(room);

        Assert.Equal(96, seats.Count);
        Assert.Equal(96, seats.Select(s => (s.RowNumber, s.ColumnNumber)).Distinct().Count());
        Assert.All(seats, s => Assert.Equal(room.Id, s.RoomId));
    }

    [Fact]
    public void Generate_labels_seats_from_A1()
    {
        var seats = SeatGenerator.Generate(new Room { Id = 1, TotalRows = 3, TotalColumns = 4 });

        Assert.Equal("A1", seats.First().Label);
        Assert.Equal("C4", seats.Last().Label);
    }

    [Fact]
    public void Generate_puts_couple_on_last_row_and_vip_in_the_middle()
    {
        var seats = SeatGenerator.Generate(new Room { Id = 1, TotalRows = 8, TotalColumns = 2 });

        Assert.All(seats.Where(s => s.RowNumber == 8), s => Assert.Equal(SeatType.Couple, s.SeatType));
        Assert.All(seats.Where(s => s.RowNumber is 3 or 4 or 5), s => Assert.Equal(SeatType.Vip, s.SeatType));
        Assert.All(seats.Where(s => s.RowNumber is 1 or 2 or 6 or 7), s => Assert.Equal(SeatType.Standard, s.SeatType));
    }

    [Fact]
    public async Task Duplicate_seat_position_in_a_room_is_rejected_by_the_database()
    {
        // SQLite in-memory thay cho InMemory provider: chỉ provider quan hệ mới thật sự
        // áp unique index, InMemory sẽ cho ghi trùng và test hoá ra vô nghĩa.
        using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CinemaDbContext>().UseSqlite(connection).Options;
        using var db = new CinemaDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var room = new Room { TheaterId = 1, RoomName = "Phòng 1", TotalRows = 2, TotalColumns = 2 };
        db.Theaters.Add(new Theater { Id = 1, Name = "CineSmart", Rooms = { room } });
        await db.SaveChangesAsync();

        db.Seats.AddRange(SeatGenerator.Generate(room));
        await db.SaveChangesAsync();

        db.Seats.Add(new Seat { RoomId = room.Id, RowNumber = 1, ColumnNumber = 1 });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // Khẳng định đúng lý do: unique index, không phải một ràng buộc nào khác tình cờ nổ.
        Assert.Contains("UNIQUE", ex.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }
}
