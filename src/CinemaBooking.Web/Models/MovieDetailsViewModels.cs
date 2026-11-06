namespace CinemaBooking.Web.Models;

/// <summary>
/// Trang chi tiết phim: thông tin phim + suất chiếu của MỘT ngày, gom theo chi nhánh.
/// Khách chọn phim trước rồi mới chọn nơi xem, nên cùng một phim hiện đủ mọi chi nhánh có chiếu.
/// </summary>
public sealed record MovieDetailsViewModel(
    MovieCardViewModel Movie,
    string? Description,
    DateOnly Today,
    IReadOnlyList<DateOnly> Dates,
    DateOnly? SelectedDate,
    ShowtimeFilter Filter,
    IReadOnlyList<BranchShowtimesViewModel> Branches,
    MovieExtraInfo? Extra = null)
{
    public bool HasShowtimes => Branches.Count > 0;

    /// <summary>Nhãn tab ngày: "Hôm nay", "Ngày mai", còn lại "T4 · 14/10".</summary>
    public string DateLabel(DateOnly date) => (date.DayNumber - Today.DayNumber) switch
    {
        0 => "Hôm nay",
        1 => "Ngày mai",
        _ => $"{WeekdayShort(date.DayOfWeek)} · {date:dd/MM}",
    };

    // Không dựa vào CultureInfo: app chưa bật localization, "ddd" sẽ ra "Wed".
    private static string WeekdayShort(DayOfWeek d) => d == DayOfWeek.Sunday ? "CN" : $"T{(int)d + 1}";
}

/// <summary>Bộ lọc thành phố / chi nhánh đang chọn và các lựa chọn để vẽ chip.</summary>
public sealed record ShowtimeFilter(
    string? City,
    int? TheaterId,
    IReadOnlyList<string> Cities,
    IReadOnlyList<BranchOption> Branches)
{
    public bool IsActive => City is not null || TheaterId is not null;
}

public sealed record BranchOption(int Id, string Name, string City);

public sealed record BranchShowtimesViewModel(
    int TheaterId,
    string TheaterName,
    string City,
    IReadOnlyList<ShowtimeSlotViewModel> Showtimes);

/// <summary>Một nút giờ chiếu. Giờ đã đổi sang giờ rạp — view không tự cộng offset.</summary>
public sealed record ShowtimeSlotViewModel(
    int Id,
    string RoomName,
    DateTime StartLocal,
    DateTime EndLocal,
    decimal BasePrice,
    string FormatLabel = "");

/// <summary>Thông tin thêm của phim: độ tuổi, đạo diễn, diễn viên, trailer.</summary>
public sealed record MovieExtraInfo(
    CinemaBooking.Domain.Enums.AgeRating AgeRating, string? Director, string? Cast, string? TrailerUrl)
{
    /// <summary>Chỉ nhận link http/https để không nhúng được "javascript:" vào href.</summary>
    public string? SafeTrailerUrl =>
        Uri.TryCreate(TrailerUrl, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? u.AbsoluteUri
            : null;
}
