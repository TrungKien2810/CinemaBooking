using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;

namespace CinemaBooking.Tests;

public class SeatMapViewModelTests
{
    private static readonly DateTime StartLocal = new(2026, 10, 2, 19, 0, 0);

    /// <summary>Suất chiếu với đồ thị đã nạp sẵn như controller Include — không cần DB.</summary>
    private static Showtime BuildShowtime(int rows, int columns, decimal basePrice = 100_000m)
    {
        var theater = new Theater { Id = 1, Name = "CineSmart Cầu Giấy", City = "Hà Nội" };
        var room = new Room { Id = 7, Theater = theater, TheaterId = 1, RoomName = "Phòng A", TotalRows = rows, TotalColumns = columns };

        var id = 100;
        foreach (var seat in SeatGenerator.Generate(room))
        {
            seat.Id = id++;
            room.Seats.Add(seat);
        }

        return new Showtime
        {
            Id = 42,
            Movie = new Movie { Title = "Mắt Biếc", DurationMinutes = 117 },
            Room = room,
            BasePrice = basePrice,
        };
    }

    private static SeatMapViewModel Build(Showtime showtime, params int[] booked) =>
        SeatMapViewModel.Build(showtime, booked.ToHashSet(), new SeatPricingService(), StartLocal);

    [Fact]
    public void Grid_has_one_row_per_room_row_and_one_cell_per_column()
    {
        var model = Build(BuildShowtime(rows: 4, columns: 6));

        Assert.Equal(4, model.Rows.Count);
        Assert.Equal(6, model.TotalColumns);
        Assert.All(model.Rows, r => Assert.Equal(6, r.Seats.Count));
        Assert.Equal(["A", "B", "C", "D"], model.Rows.Select(r => r.Label));

        // Hàng D là ghế đôi: 6 ghế → 3 khối, nửa phải của mỗi khối chỉ giữ chỗ trong lưới.
        Assert.Equal("D5–D6", model.Rows[3].Seats[4]!.Label);
        Assert.True(model.Rows[3].Seats[5]!.IsCoveredByPair);
    }

    [Fact]
    public void Couple_seats_merge_into_one_block_priced_as_two_tickets()
    {
        // 6 hàng: hàng F (cuối) là ghế đôi, 6 cột → 3 khối J1–J2, J3–J4, J5–J6.
        var model = Build(BuildShowtime(rows: 6, columns: 6, basePrice: 100_000m));
        var row = model.Rows[5];

        Assert.Equal("F1–F2", row.Seats[0]!.Label);
        Assert.Equal("F3–F4", row.Seats[2]!.Label);
        Assert.Equal("F5–F6", row.Seats[4]!.Label);

        Assert.True(row.Seats[1]!.IsCoveredByPair);
        Assert.True(row.Seats[3]!.IsCoveredByPair);
        Assert.True(row.Seats[5]!.IsCoveredByPair);

        var pair = row.Seats[0]!;
        Assert.True(pair.IsPair);
        Assert.Equal(2, pair.SeatIds.Count);
        // Cả cặp = 2 vé thường, KHÔNG phải 2 lần giá ghế đôi.
        Assert.Equal(200_000m, pair.Price);
    }

    [Fact]
    public void Odd_number_of_couple_seats_leaves_an_unbookable_orphan()
    {
        // Hàng B có 3 ghế đôi: B1–B2 ghép cặp, B3 lẻ loi. (SeatGenerator không còn tự sinh ghế lẻ —
        // phòng cũ hoặc sửa tay mới có, nên dựng bằng tay ở đây.)
        var showtime = BuildShowtime(rows: 2, columns: 3);
        showtime.Room!.Seats.Single(s => s.Label == "B3").SeatType = SeatType.Couple;
        var model = Build(showtime);

        Assert.Equal("B1–B2", model.Rows[1].Seats[0]!.Label);
        Assert.True(model.Rows[1].Seats[2]!.IsOrphan);
        Assert.False(model.Rows[1].Seats[2]!.IsPair);
    }

    [Fact]
    public void Only_seats_in_the_booked_set_are_marked_booked()
    {
        var showtime = BuildShowtime(rows: 3, columns: 3);
        var a2 = showtime.Room!.Seats.Single(s => s.Label == "A2");
        var c3 = showtime.Room.Seats.Single(s => s.Label == "C3");

        var model = Build(showtime, a2.Id, c3.Id);

        var booked = model.Rows.SelectMany(r => r.Seats).Where(s => s!.IsBooked).Select(s => s!.Label);
        Assert.Equal(["A2", "C3"], booked);
    }

    [Fact]
    public void Missing_seat_position_stays_an_empty_cell_so_columns_line_up()
    {
        var showtime = BuildShowtime(rows: 2, columns: 4);
        var b2 = showtime.Room!.Seats.Single(s => s.Label == "B2");
        showtime.Room.Seats.Remove(b2);

        var model = Build(showtime);

        Assert.Null(model.Rows[1].Seats[1]);
        // B1 bị khuyết bạn đồng hàng liền kề → lẻ loi; B3–B4 liền kề nên ghép thành một cặp.
        Assert.True(model.Rows[1].Seats[0]!.IsOrphan);
        Assert.Equal("B3–B4", model.Rows[1].Seats[2]!.Label);
        Assert.Equal(4, model.Rows[1].Seats.Count);
    }

    [Fact]
    public void Seat_price_follows_seat_type()
    {
        // 6 hàng: A-B thường, C-D VIP, E thường, F ghế đôi (quy ước của SeatGenerator).
        var model = Build(BuildShowtime(rows: 6, columns: 2, basePrice: 100_000m));
        var byLabel = model.Rows.SelectMany(r => r.Seats)
            .Where(s => s is { IsCoveredByPair: false })
            .ToDictionary(s => s!.Label, s => s!);

        Assert.Equal((SeatType.Standard, 100_000m), (byLabel["A1"].Type, byLabel["A1"].Price));
        Assert.Equal((SeatType.Vip, 120_000m), (byLabel["C1"].Type, byLabel["C1"].Price));

        // Ghế đôi hiển thị theo cặp: mỗi nửa một vé thường → cả cặp F1–F2 = 200.000.
        Assert.Equal((SeatType.Couple, 200_000m), (byLabel["F1–F2"].Type, byLabel["F1–F2"].Price));
    }

    [Fact]
    public void Header_carries_branch_room_and_local_start_time()
    {
        var model = Build(BuildShowtime(rows: 1, columns: 1));

        Assert.Equal(42, model.ShowtimeId);
        Assert.Equal("Mắt Biếc", model.MovieTitle);
        Assert.Equal("CineSmart Cầu Giấy", model.TheaterName);
        Assert.Equal("Hà Nội", model.City);
        Assert.Equal("Phòng A", model.RoomName);
        Assert.Equal(StartLocal, model.StartLocal);
    }

    [Fact]
    public void Building_without_the_room_loaded_fails_loudly()
    {
        var showtime = BuildShowtime(rows: 1, columns: 1);
        showtime.Room = null;

        Assert.Throws<ArgumentException>(() => Build(showtime));
    }
}
