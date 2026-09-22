using CinemaBooking.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Infrastructure.Persistence;

/// <summary>
/// Dữ liệu mẫu cho catalog: phim, chi nhánh, phòng, suất chiếu.
/// Tách riêng khỏi IdentitySeeder (tài khoản và vai trò) để hai người sửa hai file khác nhau.
/// </summary>
public static class CatalogSeeder
{
    /// <summary>Số ngày sinh lịch chiếu kể từ hôm nay, để demo luôn có suất ở tương lai.</summary>
    private const int DaysAhead = 7;

    /// <summary>
    /// Giờ chiếu dưới đây viết theo giờ Việt Nam cho dễ đọc, trừ offset để lưu UTC.
    /// Trùng với mặc định của <c>Display:TimeZoneOffsetHours</c> ở Web.
    /// </summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>
    /// Lịch riêng của một chi nhánh: khung giờ, giá gốc và độ lệch khi xoay vòng phim.
    /// Mỗi chi nhánh một bộ khác nhau — rạp thật xếp suất ở từng chi nhánh (AD-4).
    /// Các khung giờ cách nhau ≥ 3h15 để phim dài nhất (166') cộng thời gian dọn phòng không chồng suất sau.
    /// </summary>
    private sealed record BranchSchedule(TimeOnly[] StartTimes, decimal BasePrice, int MovieOffset);

    public static async Task SeedAsync(CinemaDbContext db, CancellationToken ct = default)
    {
        if (await db.Movies.AnyAsync(ct)) return;

        var movies = new List<Movie>
        {
            new()
            {
                Title = "Mắt Biếc",
                Description = "Chuyện tình đơn phương của Ngạn dành cho Hà Lan, từ làng Đo Đo lên thành phố.",
                Genre = "Tình cảm",
                DurationMinutes = 117,
                ReleaseDate = new DateOnly(2019, 12, 20),
                PosterUrl = "/img/posters/mat-biec.jpg",
            },
            new()
            {
                Title = "Nhà Bà Nữ",
                Description = "Bi hài kịch gia đình ba thế hệ quanh một quán bánh canh nhỏ.",
                Genre = "Hài",
                DurationMinutes = 106,
                ReleaseDate = new DateOnly(2023, 1, 22),
                PosterUrl = "/img/posters/nha-ba-nu.jpg",
            },
            new()
            {
                Title = "Dune: Part Two",
                Description = "Paul Atreides liên minh với người Fremen để trả thù những kẻ đã hủy diệt gia tộc mình.",
                Genre = "Khoa học viễn tưởng",
                DurationMinutes = 166,
                ReleaseDate = new DateOnly(2024, 3, 1),
                PosterUrl = "/img/posters/dune-part-two.jpg",
            },
            new()
            {
                Title = "Inside Out 2",
                Description = "Riley bước vào tuổi dậy thì và bộ não cô bé đón thêm những cảm xúc mới.",
                Genre = "Hoạt hình",
                DurationMinutes = 96,
                ReleaseDate = new DateOnly(2024, 6, 14),
                PosterUrl = "/img/posters/inside-out-2.jpg",
            },
            new()
            {
                Title = "Địa Đạo: Mặt Trời Trong Bóng Tối",
                Description = "Một đội du kích bám trụ trong hệ thống địa đạo giữa những ngày ác liệt nhất.",
                Genre = "Chiến tranh",
                DurationMinutes = 128,
                ReleaseDate = new DateOnly(2025, 4, 4),
                PosterUrl = "/img/posters/dia-dao.jpg",
            },
        };

        var theaters = new List<Theater>
        {
            new()
            {
                Name = "CineSmart Nguyễn Huệ",
                City = "TP. Hồ Chí Minh",
                Address = "72 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh",
                Rooms =
                {
                    new Room { RoomName = "Phòng 1", TotalRows = 8, TotalColumns = 12 },
                    new Room { RoomName = "Phòng 2", TotalRows = 6, TotalColumns = 10 },
                },
            },
            new()
            {
                Name = "CineSmart Thảo Điền",
                City = "TP. Hồ Chí Minh",
                Address = "12 Quốc Hương, Thảo Điền, TP. Thủ Đức",
                Rooms =
                {
                    new Room { RoomName = "Phòng 1", TotalRows = 7, TotalColumns = 10 },
                },
            },
            new()
            {
                Name = "CineSmart Cầu Giấy",
                City = "Hà Nội",
                Address = "15 Trần Duy Hưng, Cầu Giấy, Hà Nội",
                Rooms =
                {
                    new Room { RoomName = "Phòng A", TotalRows = 10, TotalColumns = 14 },
                    new Room { RoomName = "Phòng B", TotalRows = 5, TotalColumns = 8 },
                },
            },
            new()
            {
                Name = "CineSmart Hoàn Kiếm",
                City = "Hà Nội",
                Address = "8 Tràng Tiền, Hoàn Kiếm, Hà Nội",
                Rooms =
                {
                    new Room { RoomName = "Phòng 1", TotalRows = 6, TotalColumns = 9 },
                },
            },
        };

        // Cùng thứ tự với danh sách chi nhánh ở trên.
        var schedules = new List<BranchSchedule>
        {
            new([new(9, 0), new(12, 15), new(15, 30), new(18, 45), new(22, 0)], 95_000m, 0),
            new([new(10, 30), new(14, 30), new(19, 0)], 85_000m, 1),
            new([new(9, 30), new(12, 45), new(16, 0), new(19, 15), new(22, 30)], 90_000m, 2),
            new([new(11, 0), new(17, 0), new(20, 30)], 80_000m, 3),
        };

        db.Movies.AddRange(movies);
        db.Theaters.AddRange(theaters);
        await db.SaveChangesAsync(ct);

        var rooms = theaters.SelectMany(t => t.Rooms).ToList();
        db.Seats.AddRange(rooms.SelectMany(SeatGenerator.Generate));
        await db.SaveChangesAsync(ct);

        db.Showtimes.AddRange(theaters.SelectMany((t, i) => BuildShowtimes(movies, t, schedules[i])));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Mỗi phòng của chi nhánh chiếu một phim ở mỗi khung giờ, xoay vòng danh sách phim để mỗi
    /// phim đều có suất và cùng một phim xuất hiện ở nhiều chi nhánh với giờ và giá khác nhau.
    /// </summary>
    private static IEnumerable<Showtime> BuildShowtimes(List<Movie> movies, Theater theater, BranchSchedule schedule)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow + VietnamOffset);
        var rooms = theater.Rooms.ToList();

        for (var day = 0; day < DaysAhead; day++)
        {
            for (var r = 0; r < rooms.Count; r++)
            {
                for (var s = 0; s < schedule.StartTimes.Length; s++)
                {
                    var movie = movies[(day + r + s + schedule.MovieOffset) % movies.Count];
                    var localStart = today.AddDays(day).ToDateTime(schedule.StartTimes[s]);
                    var start = DateTime.SpecifyKind(localStart - VietnamOffset, DateTimeKind.Utc);

                    yield return new Showtime
                    {
                        MovieId = movie.Id,
                        RoomId = rooms[r].Id,
                        StartTimeUtc = start,
                        EndTimeUtc = start.AddMinutes(movie.DurationMinutes),
                        BasePrice = PriceFor(schedule.BasePrice, rooms[r], schedule.StartTimes[s]),
                    };
                }
            }
        }
    }

    /// <summary>Giá gốc theo chi nhánh; phòng nhỏ rẻ hơn, suất từ 18h đắt hơn — giá mẫu để trang đặt vé có số thật.</summary>
    private static decimal PriceFor(decimal branchBasePrice, Room room, TimeOnly start)
    {
        var price = room.TotalRows * room.TotalColumns >= 100 ? branchBasePrice : branchBasePrice - 15_000m;
        return start.Hour >= 18 ? price + 25_000m : price;
    }
}
