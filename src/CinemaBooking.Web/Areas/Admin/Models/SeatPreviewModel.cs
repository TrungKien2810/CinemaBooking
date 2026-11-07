using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;

namespace CinemaBooking.Web.Areas.Admin.Models;

/// <summary>
/// Sơ đồ ghế xem trước trên form phòng chiếu. Sinh bằng chính <see cref="SeatGenerator"/> mà
/// RoomsController dùng khi lưu, nên admin thấy đúng sơ đồ sẽ được tạo — không chép lại quy ước
/// loại ghế sang JavaScript để rồi hai bên lệch nhau.
/// </summary>
public sealed record SeatPreviewModel(int Rows, int Columns, IReadOnlyList<IReadOnlyList<SeatType?>> Grid)
{
    /// <summary>Cùng giới hạn với <see cref="RoomFormModel"/>: 26 hàng (A–Z) × 50 ghế.</summary>
    public const int MaxRows = 26;
    public const int MaxColumns = 50;

    /// <summary>Số ngoài khoảng hợp lệ bị kẹp lại — đang gõ dở "1" rồi "12" vẫn vẽ được.</summary>
    public static SeatPreviewModel For(
        int rows, int columns,
        SeatLayoutPreset preset = SeatLayoutPreset.Classic, int aisleAfterColumn = 0, RoomType roomType = RoomType.Standard)
    {
        rows = Math.Clamp(rows, 1, MaxRows);
        columns = Math.Clamp(columns, 1, MaxColumns);
        // Lối đi ngoài khoảng hợp lệ [2, cột − 3] thì không vẽ (form báo lỗi riêng).
        if (!SeatGenerator.IsValidAisle(aisleAfterColumn, columns)) aisleAfterColumn = 0;

        var seats = SeatGenerator.Generate(
            new Room { TotalRows = rows, TotalColumns = columns, RoomType = roomType }, preset, aisleAfterColumn);

        // Ô null = lối đi (cột không có ghế) — view chừa trống để cột vẫn thẳng hàng.
        var grid = Enumerable.Range(1, rows)
            .Select(r => (IReadOnlyList<SeatType?>)Enumerable.Range(1, columns)
                .Select(c => seats.FirstOrDefault(s => s.RowNumber == r && s.ColumnNumber == c)?.SeatType)
                .ToList())
            .ToList();

        return new SeatPreviewModel(rows, columns, grid) { Preset = preset.Resolve(roomType), HasAisle = aisleAfterColumn > 0 };
    }

    /// <summary>Mẫu thật sự được dùng (Auto đã quy đổi theo loại phòng) — để view ghi chú cho admin.</summary>
    public SeatLayoutPreset Preset { get; init; } = SeatLayoutPreset.Classic;

    public bool HasAisle { get; init; }

    public int Count(SeatType type) => Grid.Sum(row => row.Count(t => t == type));

    public int SeatTotal => Grid.Sum(row => row.Count(t => t is not null));

    public static string RowLabel(int rowIndex) => ((char)('A' + rowIndex)).ToString();
}
