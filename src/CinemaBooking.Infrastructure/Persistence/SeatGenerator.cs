using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Infrastructure.Persistence;

/// <summary>
/// Sinh danh sách ghế từ cấu hình hàng × cột của phòng. Dùng khi seed dữ liệu và khi admin tạo phòng /
/// đổi layout — cùng một cấu hình luôn cho ra cùng một sơ đồ ghế.
/// </summary>
public static class SeatGenerator
{
    /// <summary>
    /// Sơ đồ MẶC ĐỊNH (mẫu Classic): hàng cuối là ghế đôi, phần ba ở giữa phòng là ghế VIP, còn lại là ghế
    /// thường. Giữ nguyên chữ ký cũ để seed và test hiện có không đổi.
    /// </summary>
    public static List<Seat> Generate(Room room) => Generate(room, SeatLayoutPreset.Classic, 0);

    /// <summary>
    /// Sinh ghế theo <paramref name="preset"/> (Auto → theo <see cref="Room.RoomType"/>). Nếu
    /// <paramref name="aisleAfterColumn"/> ≥ 1 thì cột ngay sau đó là LỐI ĐI: không có ghế nào ở cột đó (sơ đồ
    /// khách và admin chừa trống, số cột của ghế vẫn liền mạch phía hai bên). Ghế đôi luôn đi thành cặp trong từng
    /// đoạn liền kề; ghế lẻ dư ở cuối đoạn là ghế thường.
    /// </summary>
    public static List<Seat> Generate(Room room, SeatLayoutPreset preset, int aisleAfterColumn)
    {
        var layout = preset.Resolve(room.RoomType);
        var aisleColumn = IsValidAisle(aisleAfterColumn, room.TotalColumns) ? aisleAfterColumn + 1 : 0;
        var seats = new List<Seat>(room.TotalRows * room.TotalColumns);

        for (var row = 1; row <= room.TotalRows; row++)
        {
            var coupleColumns = layout is SeatLayoutPreset.Classic or SeatLayoutPreset.CoupleBack && row == room.TotalRows
                ? CoupleColumns(room.TotalColumns, aisleColumn)
                : [];

            for (var col = 1; col <= room.TotalColumns; col++)
            {
                if (col == aisleColumn) continue;

                seats.Add(new Seat
                {
                    RoomId = room.Id,
                    RowNumber = row,
                    ColumnNumber = col,
                    SeatType = coupleColumns.Contains(col) ? SeatType.Couple : BaseType(layout, row, room.TotalRows),
                });
            }
        }

        return seats;
    }

    /// <summary>Số ghế thật sự sẽ được tạo (trừ cột lối đi) — dùng cho xem trước và kiểm tra nhanh.</summary>
    public static int SeatCount(int rows, int columns, int aisleAfterColumn) =>
        rows * (columns - (IsValidAisle(aisleAfterColumn, columns) ? 1 : 0));

    /// <summary>Số cột nhỏ nhất của phòng để chừa được lối đi (mỗi bên ít nhất <see cref="MinSeatsPerSide"/> ghế + 1 cột lối đi).</summary>
    public const int MinSeatsPerSide = 2;
    public const int MinColumnsForAisle = MinSeatsPerSide * 2 + 1;

    /// <summary>Khoảng hợp lệ của "lối đi sau cột số": [2, cột − 3]. Ngoài khoảng đó (hoặc 0) coi như không có lối đi.</summary>
    public static (int Min, int Max)? AisleRange(int totalColumns) =>
        totalColumns >= MinColumnsForAisle ? (MinSeatsPerSide, totalColumns - MinSeatsPerSide - 1) : null;

    public static bool IsValidAisle(int aisleAfterColumn, int totalColumns) =>
        AisleRange(totalColumns) is { } r && aisleAfterColumn >= r.Min && aisleAfterColumn <= r.Max;

    /// <summary>Các cột được làm ghế đôi trong hàng: ghép từng đôi trong mỗi đoạn liền kề, ghế lẻ dư bỏ ra.</summary>
    private static HashSet<int> CoupleColumns(int totalColumns, int aisleColumn)
    {
        var result = new HashSet<int>();
        var runStart = 1;
        for (var col = 1; col <= totalColumns + 1; col++)
        {
            if (col <= totalColumns && col != aisleColumn) continue;

            var length = col - runStart;
            for (var i = 0; i + 1 < length; i += 2)
            {
                result.Add(runStart + i);
                result.Add(runStart + i + 1);
            }
            runStart = col + 1;
        }
        return result;
    }

    private static SeatType BaseType(SeatLayoutPreset layout, int row, int totalRows) => layout switch
    {
        SeatLayoutPreset.AllStandard or SeatLayoutPreset.CoupleBack => SeatType.Standard,
        SeatLayoutPreset.AllVip => SeatType.Vip,
        // Classic và VipMiddle: phần ba ở giữa phòng là VIP.
        _ => row > totalRows / 3 && row <= totalRows * 2 / 3 ? SeatType.Vip : SeatType.Standard,
    };
}
