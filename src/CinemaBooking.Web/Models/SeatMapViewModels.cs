using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Web.Services;

namespace CinemaBooking.Web.Models;

/// <summary>Dữ liệu trang chọn ghế của một suất chiếu. Giờ đã đổi sang giờ rạp.</summary>
public sealed record SeatMapViewModel(
    int ShowtimeId,
    string MovieTitle,
    string TheaterName,
    string City,
    string RoomName,
    DateTime StartLocal,
    int HoldSeconds,
    int TotalColumns,
    IReadOnlyList<SeatRowViewModel> Rows)
{
    /// <summary>
    /// Dựng lưới ghế theo layout phòng: đủ <see cref="Room.TotalRows"/> hàng × <see cref="Room.TotalColumns"/> cột,
    /// ô không có ghế là null để CSS Grid vẫn thẳng cột. Cần <paramref name="showtime"/> đã nạp
    /// Movie, Room.Theater và Room.Seats.
    /// </summary>
    public static SeatMapViewModel Build(
        Showtime showtime,
        IReadOnlySet<int> bookedSeatIds,
        ISeatPricingService pricing,
        DateTime startLocal,
        int holdSeconds)
    {
        var room = showtime.Room ?? throw new ArgumentException("Chưa nạp Room của suất chiếu.", nameof(showtime));
        var theater = room.Theater ?? throw new ArgumentException("Chưa nạp Room.Theater.", nameof(showtime));
        var movie = showtime.Movie ?? throw new ArgumentException("Chưa nạp Movie của suất chiếu.", nameof(showtime));

        var byPosition = room.Seats.ToDictionary(s => (s.RowNumber, s.ColumnNumber));

        var rows = new List<SeatRowViewModel>(room.TotalRows);
        for (var row = 1; row <= room.TotalRows; row++)
        {
            var cells = new SeatCellViewModel?[room.TotalColumns];
            for (var col = 1; col <= room.TotalColumns; col++)
            {
                if (!byPosition.TryGetValue((row, col), out var seat)) continue;

                cells[col - 1] = new SeatCellViewModel(
                    seat.Id,
                    seat.Label,
                    seat.SeatType,
                    pricing.PriceFor(showtime.BasePrice, seat.SeatType),
                    bookedSeatIds.Contains(seat.Id));
            }
            rows.Add(new SeatRowViewModel(RowLabel(row), cells));
        }

        return new SeatMapViewModel(
            showtime.Id, movie.Title, theater.Name, theater.City, room.RoomName,
            startLocal, holdSeconds, room.TotalColumns, rows);
    }

    private static string RowLabel(int row) => ((char)('A' + row - 1)).ToString();
}

/// <summary>Một hàng ghế; <see cref="Seats"/> có đúng TotalColumns phần tử, null là lối đi/ô trống.</summary>
public sealed record SeatRowViewModel(string Label, IReadOnlyList<SeatCellViewModel?> Seats);

public sealed record SeatCellViewModel(int SeatId, string Label, SeatType Type, decimal Price, bool IsBooked);
