namespace CinemaBooking.Worker.SeatRelease;

/// <summary>Địa chỉ của CinemaBooking.Web để Worker mở HubConnection tới SeatHub (section <c>WebHub</c>).</summary>
public sealed class WebHubOptions
{
    public const string SectionName = "WebHub";

    /// <summary>Base URL của web; đường dẫn hub được nối thêm trong notifier.</summary>
    public string BaseUrl { get; set; } = "https://localhost:7157";
}
