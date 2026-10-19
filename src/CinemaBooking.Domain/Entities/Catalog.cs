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
}
