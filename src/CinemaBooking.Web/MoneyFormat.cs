using System.Globalization;

namespace CinemaBooking.Web;

/// <summary>
/// Định dạng tiền VNĐ kiểu Việt Nam: dấu chấm ngăn cách hàng nghìn, không số lẻ ("90.000 đ").
/// Không phụ thuộc culture của máy chủ — <c>ToString("N0")</c> trên máy culture en-US ra "90,000".
/// </summary>
public static class MoneyFormat
{
    private static readonly NumberFormatInfo Vietnamese = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3],
    };

    /// <summary>
    /// Khoảng trắng KHÔNG xuống dòng giữa số và "đ": nếu dùng dấu cách thường thì ở ô hẹp "đ" bị đẩy xuống dòng riêng
    /// (số tiền dài trên điện thoại). Dấu chấm giữa các chữ số cũng không phải điểm ngắt dòng.
    /// </summary>
    private const string Nbsp = "\u00A0";

    /// <summary>"90.000 đ" (dấu cách không ngắt dòng).</summary>
    public static string Vnd(this decimal amount) => amount.ToString("#,##0", Vietnamese) + Nbsp + "đ";

    /// <summary>
    /// Số tiền rút gọn cho thẻ thống kê hẹp: 2.074.750.000 → "2,07 tỷ đ", 337.631.000 → "337,6 tr đ"; dưới 1 triệu giữ nguyên.
    /// Luôn kèm số đầy đủ ở chỗ khác (tooltip / dòng nhỏ) để không mất độ chính xác.
    /// </summary>
    public static string VndShort(this decimal amount)
    {
        var abs = Math.Abs(amount);
        if (abs >= 1_000_000_000m) return (amount / 1_000_000_000m).ToString("0.##", Vietnamese) + Nbsp + "tỷ" + Nbsp + "đ";
        if (abs >= 1_000_000m) return (amount / 1_000_000m).ToString("0.#", Vietnamese) + Nbsp + "tr" + Nbsp + "đ";
        return amount.Vnd();
    }

    /// <summary>"90.000" (không đơn vị) cho ô bảng đã có tiêu đề cột nói rõ đơn vị.</summary>
    public static string VndNumber(this decimal amount) => amount.ToString("#,##0", Vietnamese);

    /// <summary>Giá nhập theo nghìn đồng: 90000 → "90", 92500 → "92.5".</summary>
    public static string ToThousands(this decimal amount) =>
        (amount / 1000m).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Admin gõ giá theo NGHÌN đồng cho nhanh: 90 → 90.000đ, 92.5 → 92.500đ. Số từ 1.000 trở lên coi như đã là đồng
    /// (90000 giữ nguyên) nên dán số cũ vào vẫn đúng. 0 và số âm giữ nguyên để validation báo lỗi.
    /// </summary>
    public static decimal ExpandThousands(decimal typed) => typed > 0 && typed < 1000 ? typed * 1000m : typed;
}
