using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Infrastructure.Persistence;

/// <summary>
/// Sinh danh sách ghế từ cấu hình hàng × cột của phòng. Dùng khi seed dữ liệu và khi
/// admin đổi layout phòng — cùng một phòng luôn cho ra cùng một sơ đồ ghế.
/// </summary>
public static class SeatGenerator
{
    /// <summary>
    /// Quy ước loại ghế: hàng cuối là ghế đôi, phần ba ở giữa phòng là ghế VIP,
    /// còn lại là ghế thường. Rẻ tiền nhưng đủ để sơ đồ ghế có ba màu khác nhau.
    /// </summary>
    public static List<Seat> Generate(Room room)
    {
        var seats = new List<Seat>(room.TotalRows * room.TotalColumns);

        for (var row = 1; row <= room.TotalRows; row++)
        {
            for (var col = 1; col <= room.TotalColumns; col++)
            {
                seats.Add(new Seat
                {
                    RoomId = room.Id,
                    RowNumber = row,
                    ColumnNumber = col,
                    SeatType = TypeForRow(row, room.TotalRows),
                });
            }
        }

        return seats;
    }

    private static SeatType TypeForRow(int row, int totalRows)
    {
        if (row == totalRows) return SeatType.Couple;
        if (row > totalRows / 3 && row <= totalRows * 2 / 3) return SeatType.Vip;
        return SeatType.Standard;
    }
}
