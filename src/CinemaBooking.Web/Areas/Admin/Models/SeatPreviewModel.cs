using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;

namespace CinemaBooking.Web.Areas.Admin.Models;

/// <summary>
/// Sơ đồ ghế xem trước trên form phòng chiếu. Sinh bằng chính <see cref="SeatGenerator"/> mà
/// RoomsController dùng khi lưu, nên admin thấy đúng sơ đồ sẽ được tạo — không chép lại quy ước
/// loại ghế sang JavaScript để rồi hai bên lệch nhau.
/// </summary>
public sealed record SeatPreviewModel(int Rows, int Columns, IReadOnlyList<IReadOnlyList<SeatType>> Grid)
{
    /// <summary>Cùng giới hạn với <see cref="RoomFormModel"/>: 26 hàng (A–Z) × 50 ghế.</summary>
    public const int MaxRows = 26;
    public const int MaxColumns = 50;

    /// <summary>Số ngoài khoảng hợp lệ bị kẹp lại — đang gõ dở "1" rồi "12" vẫn vẽ được.</summary>
    public static SeatPreviewModel For(int rows, int columns)
    {
        rows = Math.Clamp(rows, 1, MaxRows);
        columns = Math.Clamp(columns, 1, MaxColumns);

        var seats = SeatGenerator.Generate(new Room { TotalRows = rows, TotalColumns = columns });
        var grid = seats
            .GroupBy(s => s.RowNumber)
            .OrderBy(g => g.Key)
            .Select(g => (IReadOnlyList<SeatType>)g.OrderBy(s => s.ColumnNumber).Select(s => s.SeatType).ToList())
            .ToList();

        return new SeatPreviewModel(rows, columns, grid);
    }

    public int Count(SeatType type) => Grid.Sum(row => row.Count(t => t == type));

    public static string RowLabel(int rowIndex) => ((char)('A' + rowIndex)).ToString();
}
