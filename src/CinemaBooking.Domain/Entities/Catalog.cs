using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Domain.Entities;

public class Movie
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(2000)] public string? Description { get; set; }
    [MaxLength(100)] public string? Genre { get; set; }
    /// <summary>Thời lượng phim, tính bằng phút.</summary>
    [Range(1, 600)] public int DurationMinutes { get; set; }
    public DateOnly ReleaseDate { get; set; }
    [MaxLength(500)] public string? PosterUrl { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Phân loại độ tuổi; mặc định P (mọi lứa tuổi).</summary>
    public AgeRating AgeRating { get; set; } = AgeRating.P;
    [MaxLength(500)] public string? TrailerUrl { get; set; }
    [MaxLength(200)] public string? Director { get; set; }
    /// <summary>Diễn viên chính, phân cách bằng dấu phẩy.</summary>
    [MaxLength(500)] public string? Cast { get; set; }

    public ICollection<Showtime> Showtimes { get; set; } = new List<Showtime>();
}

/// <summary>Một chi nhánh của rạp. Lịch chiếu và giá vé đi theo chi nhánh qua Room → Showtime (AD-4).</summary>
public class Theater
{
    public int Id { get; set; }
    /// <summary>Unique toàn hệ thống — khách phân biệt chi nhánh bằng tên.</summary>
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    /// <summary>Thành phố của chi nhánh, để khách lọc suất chiếu theo nơi mình ở.</summary>
    [Required, MaxLength(100)] public string City { get; set; } = string.Empty;
    [MaxLength(500)] public string? Address { get; set; }
    /// <summary>Số điện thoại / hotline của chi nhánh (hiện cho khách ở trang chi tiết chi nhánh).</summary>
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(256)] public string? Email { get; set; }
    /// <summary>Giờ mở cửa dạng chữ, ví dụ "08:00 – 23:30 hằng ngày".</summary>
    [MaxLength(100)] public string? OpeningHours { get; set; }
    /// <summary>Giới thiệu ngắn: vị trí, tiện ích, bãi đỗ xe...</summary>
    [MaxLength(1000)] public string? Description { get; set; }
    /// <summary>Soft delete: chi nhánh đóng cửa vẫn giữ lại để đơn cũ còn tra được.</summary>
    public bool IsActive { get; set; } = true;

    // TotalRooms cố ý KHÔNG lưu: là dữ liệu dẫn xuất, đọc bằng Rooms.Count.
    public ICollection<Room> Rooms { get; set; } = new List<Room>();
}

public class Room
{
    public int Id { get; set; }
    public int TheaterId { get; set; }
    public Theater? Theater { get; set; }
    [Required, MaxLength(100)] public string RoomName { get; set; } = string.Empty;
    [Range(1, 50)] public int TotalRows { get; set; }
    [Range(1, 50)] public int TotalColumns { get; set; }
    /// <summary>Loại phòng (2D/3D/IMAX/4DX) — hiện cho khách ở lịch chiếu và vé.</summary>
    public RoomType RoomType { get; set; } = RoomType.Standard;

    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
}

public class Seat
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public Room? Room { get; set; }
    /// <summary>Hàng ghế, 1-based. Hiển thị dạng chữ cái A, B, C…</summary>
    public int RowNumber { get; set; }
    /// <summary>Cột ghế, 1-based.</summary>
    public int ColumnNumber { get; set; }
    public SeatType SeatType { get; set; } = SeatType.Standard;

    public string Label => $"{(char)('A' + RowNumber - 1)}{ColumnNumber}";
}

public class Showtime
{
    public int Id { get; set; }
    public int MovieId { get; set; }
    public Movie? Movie { get; set; }
    public int RoomId { get; set; }
    public Room? Room { get; set; }
    public DateTime StartTimeUtc { get; set; }
    /// <summary>Suy ra từ StartTimeUtc + Movie.DurationMinutes khi tạo, lưu lại để truy vấn trùng lịch.</summary>
    public DateTime EndTimeUtc { get; set; }
    public decimal BasePrice { get; set; }
    /// <summary>Phiên bản ngôn ngữ của suất (phụ đề / lồng tiếng / bản gốc).</summary>
    public ShowLanguage Language { get; set; } = ShowLanguage.Original;
    /// <summary>Tạm ẩn: suất ẩn không hiện và không bán cho khách, nhưng giữ nguyên đơn đã đặt.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Khuyến mãi riêng của một chi nhánh, hiển thị trên trang chi tiết chi nhánh. Mỗi chi nhánh tự đặt
/// chương trình của mình. Hiện chỉ là thông tin quảng bá — chưa tự trừ vào giá vé khi thanh toán.
/// </summary>
public class Promotion
{
    public int Id { get; set; }
    public int TheaterId { get; set; }
    public Theater? Theater { get; set; }
    [Required, MaxLength(150)] public string Title { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Description { get; set; }
    /// <summary>Nhãn ngắn hiện nổi bật, ví dụ "-20%", "Đồng giá 60k".</summary>
    [MaxLength(40)] public string? Badge { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsActive { get; set; } = true;

    // ── Quy tắc giảm giá tự động (các điều kiện còn lại là AND; để trống = không giới hạn) ──
    // Để trống cả DiscountPercent và FixedPrice thì khuyến mãi chỉ là thông tin quảng bá
    // (ví dụ ưu đãi cần xuất trình thẻ sinh viên tại quầy, hệ thống không tự trừ được).

    /// <summary>Giảm theo phần trăm giá vé (1–100). Loại trừ với <see cref="FixedPrice"/>.</summary>
    public int? DiscountPercent { get; set; }

    /// <summary>"Đồng giá": mỗi ghế (mỗi nửa ghế đôi) tối đa bằng mức này. Không bao giờ làm giá tăng lên.</summary>
    public decimal? FixedPrice { get; set; }

    /// <summary>Chỉ áp dụng cho loại ghế này; null = mọi loại ghế.</summary>
    public SeatType? ApplicableSeatType { get; set; }

    /// <summary>Thứ trong tuần được áp dụng: bit i ứng với <see cref="DayOfWeek"/> i (0 = Chủ nhật … 6 = Thứ bảy); 0 = mọi ngày.</summary>
    public int DaysOfWeekMask { get; set; }

    /// <summary>Giờ bắt đầu SUẤT CHIẾU (giờ rạp) phải từ mốc này trở đi; null = không giới hạn.</summary>
    public TimeOnly? FromTime { get; set; }

    /// <summary>Giờ bắt đầu suất chiếu phải TRƯỚC mốc này; null = không giới hạn.</summary>
    public TimeOnly? ToTime { get; set; }

    public bool AppliesAutomatically => DiscountPercent is > 0 || FixedPrice is > 0;
}
