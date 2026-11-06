using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Domain;

/// <summary>Phụ thu theo loại ghế của một chi nhánh (VIP, mỗi nửa ghế đôi).</summary>
public sealed record SeatSurcharges(decimal Vip, decimal Couple)
{
    public decimal For(SeatType type) => type switch
    {
        SeatType.Vip => Vip,
        SeatType.Couple => Couple,
        _ => 0m,
    };
}

/// <summary>
/// Công thức giá vé theo bảng giá chi nhánh (<see cref="TheaterPricing"/>): giá KHÔNG phụ thuộc phim mà phụ thuộc
/// loại phòng, loại ghế, khung giờ và ngày chiếu. Một nơi duy nhất để trang bảng giá, sơ đồ ghế, thanh toán,
/// xếp lịch tự động và seed dữ liệu cùng tính — tự tính riêng thì sớm muộn sẽ lệch nhau.
/// </summary>
public static class PricingRules
{
    /// <summary>Chi nhánh có thu phụ thu giờ cao điểm không (phụ thu 0 = tắt).</summary>
    public static bool PeakEnabled(TheaterPricing p) => p.PeakSurcharge > 0m;

    /// <summary>Chi nhánh có thu phụ thu cuối tuần không (phụ thu 0 = tắt).</summary>
    public static bool WeekendEnabled(TheaterPricing p) => p.WeekendSurcharge > 0m;

    /// <summary>Suất bắt đầu từ giờ cao điểm trở đi.</summary>
    public static bool IsPeak(TheaterPricing p, DateTime startLocal) => startLocal.Hour >= p.PeakFromHour;

    /// <summary>Cuối tuần: Thứ 7, Chủ nhật, và Thứ 6 từ giờ cao điểm.</summary>
    public static bool IsWeekend(TheaterPricing p, DateTime startLocal) =>
        startLocal.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
        || (startLocal.DayOfWeek == DayOfWeek.Friday && IsPeak(p, startLocal));

    /// <summary>Giá ghế THƯỜNG của một suất (giá "cơ bản" lưu ở <c>Showtime.BasePrice</c>).</summary>
    public static decimal BasePrice(TheaterPricing p, RoomType room, DateTime startLocal) =>
        p.RoomPrice(room)
        + (IsPeak(p, startLocal) ? p.PeakSurcharge : 0m)
        + (IsWeekend(p, startLocal) ? p.WeekendSurcharge : 0m);

    /// <summary>Giá MỘT ghế (một nửa ghế đôi) = giá cơ bản của suất + phụ thu loại ghế.</summary>
    public static decimal SeatPrice(TheaterPricing p, RoomType room, SeatType seat, DateTime startLocal) =>
        BasePrice(p, room, startLocal) + Surcharges(p).For(seat);

    public static SeatSurcharges Surcharges(TheaterPricing p) => new(p.VipSurcharge, p.CoupleSurcharge);

    /// <summary>
    /// Bảng giá mặc định cho chi nhánh có giá ghế thường 2D là <paramref name="standard"/>: 3D +20k, IMAX +45k, 4DX +55k,
    /// VIP +20k, ghế đôi +10k mỗi ghế, giờ cao điểm (từ 18:00) +25k, cuối tuần +10k.
    /// </summary>
    public static TheaterPricing DefaultFor(int theaterId, decimal standard) => new()
    {
        TheaterId = theaterId,
        StandardPrice = standard,
        ThreeDPrice = standard + 20_000m,
        ImaxPrice = standard + 45_000m,
        FourDxPrice = standard + 55_000m,
        VipSurcharge = 20_000m,
        CoupleSurcharge = 10_000m,
        PeakSurcharge = 25_000m,
        PeakFromHour = 18,
        WeekendSurcharge = 10_000m,
    };
}
