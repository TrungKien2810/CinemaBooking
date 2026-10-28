using System.Text.Json;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Giỏ ghế giữa trang chọn ghế và trang thanh toán. Lưu ở Session (server-side) để bước đặt vé
/// không phải tin danh sách ghế client gửi lại (General.md §6.2). <see cref="ConnectionId"/> là
/// owner token của lock — đã chuyển từ connection SignalR sang hold-token trong Session khi
/// khách bấm "Tiếp tục thanh toán", để rời trang không làm mất ghế.
/// </summary>
public sealed record SeatSelection(int ShowtimeId, IReadOnlyList<int> SeatIds, string ConnectionId);

public static class SeatSelectionSessionExtensions
{
    private const string Key = "Booking.SeatSelection";

    public static void SetSeatSelection(this ISession session, SeatSelection selection) =>
        session.SetString(Key, JsonSerializer.Serialize(selection));

    public static SeatSelection? GetSeatSelection(this ISession session) =>
        session.GetString(Key) is { } json ? JsonSerializer.Deserialize<SeatSelection>(json) : null;

    public static void ClearSeatSelection(this ISession session) => session.Remove(Key);
}
