namespace CinemaBooking.Web.Services;

/// <summary>Một suất chiếu đã phẳng hoá đủ để render, không kéo theo entity graph.</summary>
public sealed record ShowtimeListItem(
    int Id,
    int MovieId,
    string MovieTitle,
    int RoomId,
    string RoomName,
    int TheaterId,
    string TheaterName,
    string City,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    decimal BasePrice);
