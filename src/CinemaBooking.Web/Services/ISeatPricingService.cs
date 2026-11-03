using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Giá một ghế của một suất. Một nơi duy nhất biết công thức: sơ đồ ghế hiển thị giá, và bước
/// đặt vé (Sprint 4) ghi <c>PriceAtBooking</c> — hai chỗ tự tính riêng thì sớm muộn sẽ lệch nhau.
/// </summary>
public interface ISeatPricingService
{
    decimal PriceFor(decimal basePrice, SeatType type);
}

/// <summary>
/// Ghế VIP +20%. Ghế đôi: MỖI NỬA tính bằng một vé thường — cả cặp (2 nửa) = 2 vé thường.
/// Không nhân đôi từng nửa, vì như vậy một cặp sẽ thành 4 vé (xem <see cref="SeatPairing"/>).
/// Không giữ trạng thái — đăng ký Singleton.
/// </summary>
public sealed class SeatPricingService : ISeatPricingService
{
    public const decimal VipMultiplier = 1.2m;

    /// <summary>Giá một NỬA ghế đôi so với vé thường. Muốn ghế đôi có phụ thu thì nâng số này
    /// (ví dụ 1.1 → cả cặp = 2,2 vé thường).</summary>
    public const decimal CoupleHalfMultiplier = 1m;

    public decimal PriceFor(decimal basePrice, SeatType type) => type switch
    {
        SeatType.Vip => basePrice * VipMultiplier,
        SeatType.Couple => basePrice * CoupleHalfMultiplier,
        _ => basePrice,
    };
}
