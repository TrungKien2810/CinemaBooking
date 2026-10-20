namespace CinemaBooking.Domain.Abstractions;

/// <summary>
/// Phát tín hiệu "ghế vừa trống" ra khỏi process Worker. Worker chạy ngoài process web nên không
/// gọi thẳng <c>IHubContext</c> được — nó bắn qua một <c>HubConnection</c> tới SeatHub. Tách thành
/// abstraction để <c>ExpiredSeatCleaner</c> không phụ thuộc SignalR và test được.
/// </summary>
public interface ISeatReleaseNotifier
{
    /// <summary>Các ghế của một suất vừa trống; gọi một lần cho mỗi suất để client vẽ lại một lượt.</summary>
    Task NotifyAsync(int showtimeId, IReadOnlyList<int> seatIds, CancellationToken ct = default);
}
