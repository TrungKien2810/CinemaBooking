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

/// <summary>Ghế VIP +20%, ghế đôi tính giá hai người. Không giữ trạng thái — đăng ký Singleton.</summary>
public sealed class SeatPricingService : ISeatPricingService
{
    public const decimal VipMultiplier = 1.2m;
    public const decimal CoupleMultiplier = 2m;

    public decimal PriceFor(decimal basePrice, SeatType type) => type switch
    {
        SeatType.Vip => basePrice * VipMultiplier,
        SeatType.Couple => basePrice * CoupleMultiplier,
        _ => basePrice,
    };
}
