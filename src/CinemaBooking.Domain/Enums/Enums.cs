namespace CinemaBooking.Domain.Enums;

public enum SeatType { Standard = 0, Vip = 1, Couple = 2 }

/// <summary>Loại phòng chiếu: quyết định trải nghiệm (và thường là giá) khách nhìn thấy khi chọn suất.</summary>
public enum RoomType { Standard = 0, ThreeD = 1, Imax = 2, FourDx = 3 }

/// <summary>
/// Mẫu sơ đồ ghế khi tạo phòng. Phòng nào cũng có thể không có hàng VIP hay ghế đôi — admin chọn mẫu phù hợp,
/// hoặc để <see cref="Auto"/> cho hệ thống chọn theo <see cref="RoomType"/>.
/// </summary>
public enum SeatLayoutPreset
{
    /// <summary>Tự chọn theo loại phòng: 2D → Classic, 3D/IMAX → VipMiddle, 4DX → AllStandard.</summary>
    Auto = 0,
    /// <summary>Hàng giữa VIP + hàng cuối ghế đôi, còn lại thường.</summary>
    Classic = 1,
    /// <summary>Toàn bộ ghế thường.</summary>
    AllStandard = 2,
    /// <summary>Hàng giữa VIP, không có ghế đôi.</summary>
    VipMiddle = 3,
    /// <summary>Ghế thường + hàng cuối ghế đôi, không có VIP.</summary>
    CoupleBack = 4,
    /// <summary>Toàn bộ ghế VIP (phòng cao cấp).</summary>
    AllVip = 5,
}

/// <summary>Ngôn ngữ của một suất chiếu: phim gốc tiếng Việt, phụ đề Việt, hay lồng tiếng Việt.</summary>
public enum ShowLanguage { Original = 0, Subtitled = 1, Dubbed = 2 }

/// <summary>Phân loại độ tuổi theo cách rạp Việt Nam hay ghi: P mọi lứa tuổi, K dưới 13 cần người bảo hộ, T13/T16/T18.</summary>
public enum AgeRating { P = 0, K = 1, T13 = 2, T16 = 3, T18 = 4 }

/// <summary>Nhãn tiếng Việt cho các enum hiển thị cho khách.</summary>
public static class EnumLabels
{
    public static string Label(this SeatLayoutPreset p) => p switch
    {
        SeatLayoutPreset.Classic => "Cổ điển: VIP giữa + ghế đôi hàng cuối",
        SeatLayoutPreset.AllStandard => "Toàn ghế thường",
        SeatLayoutPreset.VipMiddle => "VIP ở giữa, không ghế đôi",
        SeatLayoutPreset.CoupleBack => "Ghế thường + ghế đôi hàng cuối (không VIP)",
        SeatLayoutPreset.AllVip => "Toàn ghế VIP (phòng cao cấp)",
        _ => "Tự động theo loại phòng",
    };

    /// <summary>Mẫu thật sự dùng: <see cref="SeatLayoutPreset.Auto"/> được quy đổi theo loại phòng.</summary>
    public static SeatLayoutPreset Resolve(this SeatLayoutPreset preset, RoomType type) =>
        preset != SeatLayoutPreset.Auto
            ? preset
            : type switch
            {
                RoomType.ThreeD or RoomType.Imax => SeatLayoutPreset.VipMiddle,
                RoomType.FourDx => SeatLayoutPreset.AllStandard,
                _ => SeatLayoutPreset.Classic,
            };

    public static string Label(this SeatType t) => t switch
    {
        SeatType.Vip => "Ghế VIP",
        SeatType.Couple => "Ghế đôi",
        _ => "Ghế thường",
    };

    public static string Label(this BookingStatus s) => s switch
    {
        BookingStatus.Confirmed => "Đã xác nhận",
        BookingStatus.Pending => "Đang chờ thanh toán",
        BookingStatus.Cancelled => "Đã hủy",
        BookingStatus.Expired => "Đã hết hạn",
        _ => s.ToString(),
    };

    public static string Label(this PaymentStatus s) => s switch
    {
        PaymentStatus.Succeeded => "Đã thanh toán",
        PaymentStatus.Pending => "Đang chờ",
        PaymentStatus.Failed => "Thất bại",
        PaymentStatus.Refunded => "Đã hoàn tiền",
        PaymentStatus.RefundPending => "Chờ hoàn tiền",
        PaymentStatus.RefundFailed => "Hoàn tiền thất bại",
        _ => s.ToString(),
    };

    public static string Label(this RoomType t) => t switch
    {
        RoomType.ThreeD => "3D",
        RoomType.Imax => "IMAX",
        RoomType.FourDx => "4DX",
        _ => "2D",
    };

    public static string Label(this ShowLanguage l) => l switch
    {
        ShowLanguage.Subtitled => "Phụ đề",
        ShowLanguage.Dubbed => "Lồng tiếng",
        _ => "Bản gốc",
    };

    public static string Label(this AgeRating r) => r.ToString();

    public static string Describe(this AgeRating r) => r switch
    {
        AgeRating.P => "P — mọi lứa tuổi",
        AgeRating.K => "K — dưới 13 tuổi cần có người bảo hộ",
        AgeRating.T13 => "T13 — từ 13 tuổi",
        AgeRating.T16 => "T16 — từ 16 tuổi",
        AgeRating.T18 => "T18 — từ 18 tuổi",
        _ => r.ToString(),
    };
}

public enum BookingStatus { Pending = 0, Confirmed = 1, Cancelled = 2, Expired = 3 }

// Giữ nguyên giá trị cũ (Refunded = 3) để dữ liệu đang có không bị đổi nghĩa khi thêm trạng thái mới.
public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Refunded = 3,
    RefundPending = 4,
    RefundFailed = 5,
}

public enum OutboxStatus { Pending = 0, Sent = 1, Failed = 2 }
