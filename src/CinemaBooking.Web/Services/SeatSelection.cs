using System.Text.Json;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Giỏ ghế giữa trang chọn ghế và trang thanh toán. Lưu ở Session (server-side) để bước đặt vé
/// không phải tin danh sách ghế client gửi lại (General.md §6.2). Ghế được giữ ở SeatHub (SeatHoldService);
/// <see cref="ExpiresUtc"/> chỉ là hạn kỹ thuật của Session (không hiển thị như đồng hồ giữ ghế).
/// </summary>
public sealed record SeatSelection(
    int ShowtimeId,
    IReadOnlyList<int> SeatIds,
    DateTime? ExpiresUtc = null);

public static class SeatSelectionSessionExtensions
{
    private const string Key = "Booking.SeatSelection";

    public static void SetSeatSelection(this ISession session, SeatSelection selection) =>
        session.SetString(Key, JsonSerializer.Serialize(selection));

    public static SeatSelection? GetSeatSelection(this ISession session) =>
        session.GetString(Key) is { } json ? JsonSerializer.Deserialize<SeatSelection>(json) : null;

    public static void ClearSeatSelection(this ISession session)
    {
        session.Remove(Key);
        session.Remove(VoucherKey);   // mã giảm giá gắn với giỏ ghế đang chọn
    }

    private const string VoucherKey = "Booking.VoucherCode";

    public static void SetVoucherCode(this ISession session, string code) => session.SetString(VoucherKey, code);

    public static string? GetVoucherCode(this ISession session) => session.GetString(VoucherKey);

    public static void ClearVoucherCode(this ISession session) => session.Remove(VoucherKey);
}
