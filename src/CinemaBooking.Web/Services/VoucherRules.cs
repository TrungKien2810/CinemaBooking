using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

public enum VoucherCheck
{
    Ok,
    Empty,
    NotFound,
    Inactive,
    NotStarted,
    Expired,
    WrongBranch,
    BelowMinimum,
    UsageLimitReached,
    CustomerLimitReached,
}

/// <summary>Kết quả kiểm tra một mã giảm giá: dùng được hay không, giảm bao nhiêu, và lý do bằng tiếng Việt.</summary>
public sealed record VoucherResult(VoucherCheck Check, Voucher? Voucher, decimal Discount, string Message)
{
    public bool IsOk => Check == VoucherCheck.Ok;
}

/// <summary>
/// Quy tắc mã giảm giá, thuần hàm để test không cần DB. Mã được tính trên TỔNG tiền vé (sau khuyến mãi theo suất/ghế),
/// không bao giờ làm đơn xuống 0đ (cổng thanh toán từ chối số tiền 0).
/// </summary>
public static class VoucherRules
{
    /// <summary>Đơn sau giảm không được thấp hơn mức này.</summary>
    public const decimal MinPayable = 1_000m;

    public static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public static VoucherResult Evaluate(
        Voucher? v, DateOnly today, int theaterId, decimal subtotal, int totalUsed, int customerUsed)
    {
        if (v is null)
            return Fail(VoucherCheck.NotFound, "Mã giảm giá không tồn tại. Vui lòng kiểm tra lại.");
        if (!v.IsActive)
            return Fail(VoucherCheck.Inactive, "Mã giảm giá này đang tạm ngưng.", v);
        if (today < v.StartDate)
            return Fail(VoucherCheck.NotStarted, $"Mã chỉ có hiệu lực từ ngày {v.StartDate:dd/MM/yyyy}.", v);
        if (today > v.EndDate)
            return Fail(VoucherCheck.Expired, $"Mã đã hết hạn từ ngày {v.EndDate:dd/MM/yyyy}.", v);
        if (v.TheaterId is { } only && only != theaterId)
            return Fail(VoucherCheck.WrongBranch, "Mã này không áp dụng cho chi nhánh của suất chiếu.", v);
        if (v.MinOrderAmount is { } min && subtotal < min)
            return Fail(VoucherCheck.BelowMinimum, $"Đơn cần tối thiểu {min.Vnd()} để dùng mã này.", v);
        if (v.UsageLimit is { } limit && totalUsed >= limit)
            return Fail(VoucherCheck.UsageLimitReached, "Mã đã hết lượt sử dụng.", v);
        if (v.PerCustomerLimit > 0 && customerUsed >= v.PerCustomerLimit)
            return Fail(VoucherCheck.CustomerLimitReached,
                v.PerCustomerLimit == 1 ? "Bạn đã dùng mã này rồi." : $"Bạn đã dùng mã này tối đa {v.PerCustomerLimit} lần.", v);

        var discount = ComputeDiscount(v, subtotal);
        return discount <= 0
            ? Fail(VoucherCheck.BelowMinimum, "Mã này chưa giảm được cho đơn hiện tại.", v)
            : new VoucherResult(VoucherCheck.Ok, v, discount, $"Đã áp dụng mã {v.Code}: giảm {discount.Vnd()}.");
    }

    /// <summary>Số tiền giảm cho đơn <paramref name="subtotal"/> (đã bị chặn bởi trần, tổng đơn và mức thanh toán tối thiểu).</summary>
    public static decimal ComputeDiscount(Voucher v, decimal subtotal)
    {
        decimal discount;
        if (v.DiscountPercent is > 0)
        {
            discount = Math.Round(subtotal * Math.Min(v.DiscountPercent.Value, 100) / 100m, 0, MidpointRounding.AwayFromZero);
            if (v.MaxDiscountAmount is { } cap) discount = Math.Min(discount, cap);
        }
        else if (v.DiscountAmount is > 0)
        {
            discount = v.DiscountAmount.Value;
        }
        else
        {
            return 0m;
        }

        // Không giảm quá tổng đơn, và luôn để lại ít nhất MinPayable.
        return Math.Max(0m, Math.Min(discount, subtotal - MinPayable));
    }

    /// <summary>
    /// Chia <paramref name="discount"/> cho từng ghế theo tỷ lệ giá (làm tròn đồng, phần dư dồn vào ghế cuối) để tổng các
    /// ghế đúng bằng tổng đơn sau giảm — doanh thu tính theo giá từng ghế nên phải khớp.
    /// </summary>
    public static IReadOnlyList<decimal> Allocate(decimal discount, IReadOnlyList<decimal> prices)
    {
        var total = prices.Sum();
        if (discount <= 0 || total <= 0) return prices.Select(_ => 0m).ToList();

        var shares = new List<decimal>(prices.Count);
        decimal given = 0m;
        for (var i = 0; i < prices.Count; i++)
        {
            var share = i == prices.Count - 1
                ? discount - given
                : Math.Min(prices[i], Math.Round(discount * prices[i] / total, 0, MidpointRounding.AwayFromZero));
            shares.Add(share);
            given += share;
        }
        return shares;
    }

    /// <summary>Mô tả ngắn quy tắc, ví dụ "Giảm 10% (tối đa 30.000 đ) · đơn từ 100.000 đ".</summary>
    public static string Describe(Voucher v)
    {
        var parts = new List<string>();
        if (v.DiscountPercent is > 0)
            parts.Add($"Giảm {v.DiscountPercent}%" + (v.MaxDiscountAmount is { } cap ? $" (tối đa {cap.Vnd()})" : ""));
        else if (v.DiscountAmount is > 0)
            parts.Add($"Giảm {v.DiscountAmount.Value.Vnd()}");
        if (v.MinOrderAmount is { } min) parts.Add($"đơn từ {min.Vnd()}");
        return string.Join(" · ", parts);
    }

    private static VoucherResult Fail(VoucherCheck check, string message, Voucher? v = null) => new(check, v, 0m, message);
}

public interface IVoucherService
{
    /// <summary>Kiểm tra mã cho đơn có tổng tiền <paramref name="subtotal"/> ở chi nhánh <paramref name="theaterId"/>.</summary>
    Task<VoucherResult> CheckAsync(
        string? code, int theaterId, decimal subtotal, string? userId, string? email, CancellationToken ct = default);
}

public sealed class VoucherService : IVoucherService
{
    private readonly CinemaDbContext _db;
    private readonly TimeProvider _clock;
    private readonly DisplayTimeZone _tz;

    public VoucherService(CinemaDbContext db, TimeProvider clock, Microsoft.Extensions.Options.IOptions<DisplayTimeZone> tz)
    {
        _db = db;
        _clock = clock;
        _tz = tz.Value;
    }

    public async Task<VoucherResult> CheckAsync(
        string? code, int theaterId, decimal subtotal, string? userId, string? email, CancellationToken ct = default)
    {
        var normalized = VoucherRules.Normalize(code);
        if (normalized.Length == 0) return new VoucherResult(VoucherCheck.Empty, null, 0m, "Nhập mã giảm giá.");

        var voucher = await _db.Vouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Code == normalized, ct);
        if (voucher is null) return VoucherRules.Evaluate(null, default, theaterId, subtotal, 0, 0);

        // Chỉ đơn đang giữ chỗ hoặc đã thanh toán mới tính lượt dùng; đơn hủy/hết hạn trả lại lượt.
        var counted = _db.Bookings.AsNoTracking()
            .Where(b => b.VoucherCode == normalized && (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed));

        var totalUsed = await counted.CountAsync(ct);
        var normalizedEmail = email?.Trim().ToLowerInvariant();
        var customerUsed = await counted.CountAsync(b =>
            (userId != null && b.UserId == userId)
            || (normalizedEmail != null && b.CustomerEmail.ToLower() == normalizedEmail), ct);

        var today = _tz.ToLocalDate(_clock.GetUtcNow().UtcDateTime);
        return VoucherRules.Evaluate(voucher, today, theaterId, subtotal, totalUsed, customerUsed);
    }
}
