using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

/// <summary>Giá một ghế sau khi xét khuyến mãi. <see cref="PromotionTitle"/> null = không có khuyến mãi nào áp dụng.</summary>
public sealed record PriceQuote(decimal ListPrice, decimal FinalPrice, string? PromotionTitle)
{
    public decimal Discount => ListPrice - FinalPrice;
    public bool HasDiscount => Discount > 0;

    public static PriceQuote NoDiscount(decimal price) => new(price, price, null);
}

/// <summary>
/// Một nơi duy nhất quyết định khuyến mãi nào được trừ vào giá vé: sơ đồ ghế (hiển thị), trang
/// thanh toán (tóm tắt) và <c>BookingService</c> (ghi <c>PriceAtBooking</c>) đều gọi vào đây — ba chỗ tự
/// tính riêng thì sớm muộn sẽ lệch nhau. Mỗi ghế chỉ được MỘT khuyến mãi (cái cho giá thấp nhất),
/// không cộng dồn, để chủ chi nhánh không vô tình tạo vé giá 0đ.
/// </summary>
public static class PromotionRules
{
    /// <summary>Trần giảm theo phần trăm: dữ liệu cũ/ghi tay có 100% cũng không tạo ra vé 0đ (VNPAY từ chối số tiền 0).</summary>
    public const int MaxPercent = 99;

    /// <summary>Khuyến mãi có đúng điều kiện cho suất bắt đầu lúc <paramref name="showStartLocal"/> và loại ghế này không.</summary>
    public static bool Applies(Promotion p, DateTime showStartLocal, SeatType seatType)
    {
        if (!p.IsActive || !p.AppliesAutomatically) return false;

        var day = DateOnly.FromDateTime(showStartLocal);
        if (day < p.StartDate || day > p.EndDate) return false;

        if (p.ApplicableSeatType is { } only && only != seatType) return false;

        if (p.DaysOfWeekMask != 0 && (p.DaysOfWeekMask & (1 << (int)showStartLocal.DayOfWeek)) == 0) return false;

        var time = TimeOnly.FromDateTime(showStartLocal);
        if (p.FromTime is { } from && time < from) return false;
        if (p.ToTime is { } to && time >= to) return false;

        return true;
    }

    /// <summary>Giá cuối của một ghế (hoặc một nửa ghế đôi) có giá niêm yết <paramref name="listPrice"/>.</summary>
    public static PriceQuote Quote(
        decimal listPrice, SeatType seatType, DateTime showStartLocal, IEnumerable<Promotion>? promotions)
    {
        var best = PriceQuote.NoDiscount(listPrice);
        if (promotions is null) return best;

        foreach (var p in promotions)
        {
            if (!Applies(p, showStartLocal, seatType)) continue;

            var discounted = p.DiscountPercent is > 0
                ? Math.Round(listPrice * (100 - Math.Min(p.DiscountPercent.Value, MaxPercent)) / 100m, 0, MidpointRounding.AwayFromZero)
                : Math.Min(listPrice, p.FixedPrice!.Value);

            if (discounted < best.FinalPrice) best = new PriceQuote(listPrice, discounted, p.Title);
        }

        return best;
    }

    /// <summary>
    /// Khuyến mãi tự áp dụng của chi nhánh còn hiệu lực vào ngày chiếu <paramref name="showDateLocal"/>.
    /// Lọc theo NGÀY CHIẾU chứ không phải ngày đặt: đặt trước suất tuần sau vẫn hưởng khuyến mãi của tuần sau.
    /// </summary>
    public static async Task<List<Promotion>> LoadForShowtimeAsync(
        CinemaDbContext db, int theaterId, DateOnly showDateLocal, CancellationToken ct = default)
    {
        var candidates = await db.Promotions.AsNoTracking()
            .Where(p => p.TheaterId == theaterId && p.IsActive
                        && p.StartDate <= showDateLocal && p.EndDate >= showDateLocal
                        && (p.DiscountPercent != null || p.FixedPrice != null))
            .ToListAsync(ct);

        return candidates;
    }

    // ── Mô tả điều kiện cho người đọc ────────────────────────────────────────

    private static readonly string[] DayNames = ["CN", "T2", "T3", "T4", "T5", "T6", "T7"];

    /// <summary>"T3, T5" — rỗng nếu áp dụng mọi ngày.</summary>
    public static string DescribeDays(int mask)
    {
        if (mask == 0) return string.Empty;
        // Hiển thị bắt đầu từ Thứ 2 cho thuận mắt người Việt.
        var order = new[] { 1, 2, 3, 4, 5, 6, 0 };
        return string.Join(", ", order.Where(d => (mask & (1 << d)) != 0).Select(d => DayNames[d]));
    }

    /// <summary>Một dòng tóm tắt điều kiện, ví dụ "Giảm 25% · T2, T3 · suất bắt đầu trước 12:00".</summary>
    public static string Describe(Promotion p)
    {
        var parts = new List<string>();
        if (p.DiscountPercent is > 0) parts.Add($"Giảm {p.DiscountPercent}%");
        else if (p.FixedPrice is > 0) parts.Add($"Đồng giá {p.FixedPrice.Value.Vnd()}");

        if (p.ApplicableSeatType is { } t)
            parts.Add(t switch { SeatType.Vip => "ghế VIP", SeatType.Couple => "ghế đôi", _ => "ghế thường" });

        var days = DescribeDays(p.DaysOfWeekMask);
        if (days.Length > 0) parts.Add(days);

        if (p.FromTime is { } from && p.ToTime is { } to) parts.Add($"suất bắt đầu {from:HH\\:mm}–{to:HH\\:mm}");
        else if (p.FromTime is { } f) parts.Add($"suất bắt đầu từ {f:HH\\:mm}");
        else if (p.ToTime is { } u) parts.Add($"suất bắt đầu trước {u:HH\\:mm}");

        return string.Join(" · ", parts);
    }
}
