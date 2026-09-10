using CinemaBooking.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Infrastructure.Persistence;

/// <summary>
/// Dữ liệu mẫu cho catalog: phim, rạp, phòng, suất chiếu.
/// Tách riêng khỏi IdentitySeeder (tài khoản và vai trò) để hai người sửa hai file khác nhau.
/// </summary>
public static class CatalogSeeder
{
    /// <summary>Giờ chiếu cố định mỗi ngày, giờ địa phương quy ước UTC cho đơn giản.</summary>
    private static readonly int[] StartHours = [10, 13, 16, 19, 22];

    /// <summary>Số ngày sinh lịch chiếu kể từ hôm nay, để demo luôn có suất ở tương lai.</summary>
    private const int DaysAhead = 7;

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
                Address = "72 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh",
                Rooms =
                {
                    new Room { RoomName = "Phòng 1", TotalRows = 8, TotalColumns = 12 },
                    new Room { RoomName = "Phòng 2", TotalRows = 6, TotalColumns = 10 },
                },
            },
            new()
            {
                Name = "CineSmart Cầu Giấy",
                Address = "15 Trần Duy Hưng, Cầu Giấy, Hà Nội",
                Rooms =
                {
                    new Room { RoomName = "Phòng A", TotalRows = 10, TotalColumns = 14 },
                    new Room { RoomName = "Phòng B", TotalRows = 5, TotalColumns = 8 },
                },
            },
        };

        db.Movies.AddRange(movies);
        db.Theaters.AddRange(theaters);
        await db.SaveChangesAsync(ct);

        var rooms = theaters.SelectMany(t => t.Rooms).ToList();
        db.Seats.AddRange(rooms.SelectMany(SeatGenerator.Generate));
        await db.SaveChangesAsync(ct);

        db.Showtimes.AddRange(BuildShowtimes(movies, rooms));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Mỗi phòng chiếu một phim khác nhau ở mỗi khung giờ, xoay vòng danh sách phim để
    /// không phòng nào trùng lịch với chính nó và mỗi phim đều có suất.
    /// </summary>
    private static List<Showtime> BuildShowtimes(List<Movie> movies, List<Room> rooms)
    {
        var today = DateTime.UtcNow.Date;
        var showtimes = new List<Showtime>();

        for (var day = 0; day < DaysAhead; day++)
        {
            for (var r = 0; r < rooms.Count; r++)
            {
                for (var s = 0; s < StartHours.Length; s++)
                {
                    var movie = movies[(day + r + s) % movies.Count];
                    var start = today.AddDays(day).AddHours(StartHours[s]);

                    showtimes.Add(new Showtime
                    {
                        MovieId = movie.Id,
                        RoomId = rooms[r].Id,
                        StartTimeUtc = start,
                        EndTimeUtc = start.AddMinutes(movie.DurationMinutes),
                        BasePrice = PriceFor(rooms[r], StartHours[s]),
                    });
                }
            }
        }

        return showtimes;
    }

    /// <summary>Phòng lớn và suất tối đắt hơn — chỉ là giá mẫu để trang đặt vé có số thật.</summary>
    private static decimal PriceFor(Room room, int startHour)
    {
        decimal price = room.TotalRows * room.TotalColumns >= 100 ? 90_000m : 75_000m;
        return startHour >= 18 ? price + 25_000m : price;
    }
}
