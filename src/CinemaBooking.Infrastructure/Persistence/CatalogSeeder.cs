using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
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
        await SeedCatalogAsync(db, ct);
        await UpgradeCatalogDetailsAsync(db, ct);
        await SeedPromotionsAsync(db, ct);
    }

    /// <summary>Thông tin thêm của phim mẫu (độ tuổi, đạo diễn, diễn viên, trailer).</summary>
    private static readonly Dictionary<string, (AgeRating Age, string Director, string Cast, string Trailer, bool Foreign)> MovieDetails = new()
    {
        ["Mắt Biếc"] = (AgeRating.T13, "Victor Vũ", "Trần Nghĩa, Trúc Anh, Khánh Vân", "https://www.youtube.com/results?search_query=M%E1%BA%AFt+Bi%E1%BA%BFc+trailer", false),
        ["Nhà Bà Nữ"] = (AgeRating.T13, "Trấn Thành", "Lê Giang, Uyển Ân, Trấn Thành", "https://www.youtube.com/results?search_query=Nh%C3%A0+B%C3%A0+N%E1%BB%AF+trailer", false),
        ["Dune: Part Two"] = (AgeRating.T13, "Denis Villeneuve", "Timothée Chalamet, Zendaya, Rebecca Ferguson", "https://www.youtube.com/results?search_query=Dune+Part+Two+trailer", true),
        ["Inside Out 2"] = (AgeRating.P, "Kelsey Mann", "Amy Poehler, Maya Hawke, Kensington Tallman", "https://www.youtube.com/results?search_query=Inside+Out+2+trailer", true),
        ["Địa Đạo: Mặt Trời Trong Bóng Tối"] = (AgeRating.T16, "Bùi Thạc Chuyên", "Thái Hòa, Quốc Huy, Hạ Anh", "https://www.youtube.com/results?search_query=%C4%90%E1%BB%8Ba+%C4%90%E1%BA%A1o+trailer", false),
    };

    /// <summary>Loại phòng mẫu: phòng lớn nhất của Cầu Giấy là IMAX, Nguyễn Huệ Phòng 2 là 3D.</summary>
    private static readonly Dictionary<(string Theater, string Room), RoomType> RoomTypes = new()
    {
        [("CineSmart Cầu Giấy", "Phòng A")] = RoomType.Imax,
        [("CineSmart Nguyễn Huệ", "Phòng 2")] = RoomType.ThreeD,
    };

    /// <summary>
    /// Điền thông tin chi tiết cho dữ liệu mẫu — chạy cả trên DB cũ (chưa có các cột này). Chỉ đụng vào dòng còn
    /// ở giá trị mặc định (chưa ai nhập) nên không đè lên thứ admin đã sửa.
    /// </summary>
    private static async Task UpgradeCatalogDetailsAsync(CinemaDbContext db, CancellationToken ct)
    {
        // Chỉ chạy MỘT lần trên DB còn nguyên trạng cũ: đã có phim nào có đạo diễn (do lần nâng cấp trước hoặc do
        // admin nhập) thì thôi. Nếu không, mỗi lần khởi động sẽ ghi đè lựa chọn của admin (ví dụ đặt suất Dune về
        // "Bản gốc" cố ý) vì giá trị mặc định không phân biệt được "chưa nhập" với "chủ ý chọn".
        if (await db.Movies.AnyAsync(m => m.Director != null, ct)) return;

        var movies = await db.Movies.Where(m => m.Director == null && m.Cast == null).ToListAsync(ct);
        var foreignIds = new List<int>();
        foreach (var movie in movies)
        {
            if (!MovieDetails.TryGetValue(movie.Title, out var d)) continue;
            movie.AgeRating = d.Age;
            movie.Director = d.Director;
            movie.Cast = d.Cast;
            movie.TrailerUrl ??= d.Trailer;
            if (d.Foreign) foreignIds.Add(movie.Id);
        }

        var rooms = await db.Rooms.Include(r => r.Theater).Where(r => r.RoomType == RoomType.Standard).ToListAsync(ct);
        foreach (var room in rooms)
            if (RoomTypes.TryGetValue((room.Theater!.Name, room.RoomName), out var type)) room.RoomType = type;

        // Phim nước ngoài chiếu phụ đề; chỉ chỉnh suất còn "Bản gốc" mặc định.
        if (foreignIds.Count > 0)
        {
            var showtimes = await db.Showtimes
                .Where(s => foreignIds.Contains(s.MovieId) && s.Language == ShowLanguage.Original)
                .ToListAsync(ct);
            foreach (var s in showtimes) s.Language = ShowLanguage.Subtitled;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Khuyến mãi mẫu, mỗi chi nhánh một bộ khác nhau. Chạy cả khi catalog đã có (DB cũ chưa có bảng
    /// khuyến mãi); chỉ thêm khi bảng còn trống để không đè lên dữ liệu chủ chi nhánh đã nhập.
    /// </summary>
    private static async Task SeedPromotionsAsync(CinemaDbContext db, CancellationToken ct)
    {
        // Quy tắc giảm giá của từng chương trình mẫu. "Sinh viên" cố ý KHÔNG có quy tắc: cần xuất
        // trình thẻ tại quầy nên hệ thống không tự trừ được — chỉ là thông tin quảng bá.
        const int Tuesday = 1 << (int)DayOfWeek.Tuesday;
        const int Weekdays = (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4) | (1 << 5);
        var today = DateOnly.FromDateTime(DateTime.UtcNow + VietnamOffset);
        var plans = new Dictionary<string, (string Title, string Badge, string Description, Action<Promotion>? Rule)[]>
        {
            ["CineSmart Nguyễn Huệ"] = [("Thứ 3 đồng giá", "Đồng giá 60k", "Mọi suất chiếu thứ 3 chỉ 60.000đ/vé, mọi loại ghế thường.",
                p => { p.FixedPrice = 60_000m; p.ApplicableSeatType = SeatType.Standard; p.DaysOfWeekMask = Tuesday; })],
            ["CineSmart Thảo Điền"] = [
                ("Combo cặp đôi", "Ghế đôi -15%", "Giảm 15% khi đặt ghế đôi các suất sau 18:00.",
                    p => { p.DiscountPercent = 15; p.ApplicableSeatType = SeatType.Couple; p.FromTime = new TimeOnly(18, 0); }),
                ("Sinh viên", "-20%", "Xuất trình thẻ sinh viên tại quầy để được giảm 20%.", null)],
            ["CineSmart Cầu Giấy"] = [("Suất sớm", "-25%", "Giảm 25% cho các suất bắt đầu trước 12:00 các ngày trong tuần.",
                p => { p.DiscountPercent = 25; p.DaysOfWeekMask = Weekdays; p.ToTime = new TimeOnly(12, 0); })],
        };

        if (await db.Promotions.AnyAsync(ct))
        {
            // DB cũ: khuyến mãi mẫu đã có nhưng chưa có quy tắc giảm giá. Chỉ nâng cấp MỘT lần: đã có khuyến mãi
            // nào mang quy tắc giảm giá (do lần nâng cấp trước hoặc do chủ chi nhánh đặt) thì thôi. Nếu không, mỗi
            // lần khởi động sẽ bật lại mức giảm mà chủ chi nhánh cố ý tắt (chuyển về "chỉ quảng bá" nhưng giữ nguyên mô tả).
            if (await db.Promotions.AnyAsync(p => p.DiscountPercent != null || p.FixedPrice != null, ct)) return;

            var existing = await db.Promotions.Include(p => p.Theater)
                .Where(p => p.DiscountPercent == null && p.FixedPrice == null).ToListAsync(ct);
            foreach (var promo in existing)
            {
                if (!plans.TryGetValue(promo.Theater!.Name, out var seeded)) continue;
                var match = seeded.FirstOrDefault(s => s.Title == promo.Title && s.Description == promo.Description);
                match.Rule?.Invoke(promo);
            }
            await db.SaveChangesAsync(ct);
            return;
        }

        var theaters = await db.Theaters.Where(t => plans.Keys.Contains(t.Name)).ToListAsync(ct);
        foreach (var theater in theaters)
            foreach (var (title, badge, description, rule) in plans[theater.Name])
            {
                var promotion = new Promotion
                {
                    TheaterId = theater.Id, Title = title, Badge = badge, Description = description,
                    StartDate = today, EndDate = today.AddDays(60),
                };
                rule?.Invoke(promotion);
                db.Promotions.Add(promotion);
            }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedCatalogAsync(CinemaDbContext db, CancellationToken ct)
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
        var seats = rooms.SelectMany(SeatGenerator.Generate).ToList();
        db.Seats.AddRange(seats);
        await db.SaveChangesAsync(ct);

        var showtimes = theaters.SelectMany((t, i) => BuildShowtimes(movies, t, schedules[i])).ToList();
        db.Showtimes.AddRange(showtimes);
        await db.SaveChangesAsync(ct);

        db.Bookings.AddRange(BuildSampleBookings(rooms, seats, showtimes));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Vài đơn đã thanh toán ở suất sớm nhất ngày mai của mỗi chi nhánh, để mở trang chọn ghế
    /// là thấy ngay ghế đỏ "đã đặt" mà không phải tự đặt vé trước. Chỉ lấy ghế hàng A (ghế
    /// thường) nên giá vé bằng đúng giá gốc của suất.
    /// </summary>
    private static IEnumerable<Booking> BuildSampleBookings(List<Room> rooms, List<Seat> seats, List<Showtime> showtimes)
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow + VietnamOffset).AddDays(1);
        var theaterOfRoom = rooms.ToDictionary(r => r.Id, r => r.TheaterId);

        var targets = showtimes
            .Where(s => DateOnly.FromDateTime(s.StartTimeUtc + VietnamOffset) == tomorrow)
            .GroupBy(s => theaterOfRoom[s.RoomId])
            .Select(g => g.OrderBy(s => s.StartTimeUtc).First())
            .ToList();

        var number = 0;
        foreach (var showtime in targets)
        {
            var rowA = seats.Where(s => s.RoomId == showtime.RoomId && s.RowNumber == 1)
                .ToDictionary(s => s.ColumnNumber);

            yield return SampleBooking(++number, showtime, [rowA[4], rowA[5], rowA[6]], "Nguyễn Văn An", "an.nguyen@example.com");
            yield return SampleBooking(++number, showtime, [rowA[1], rowA[2]], "Trần Thị Bình", "binh.tran@example.com");
        }
    }

    private static Booking SampleBooking(int number, Showtime showtime, Seat[] seats, string name, string email)
    {
        var now = DateTime.UtcNow;
        var total = showtime.BasePrice * seats.Length;

        return new Booking
        {
            BookingCode = $"CB-SEED{number:00}",
            ShowtimeId = showtime.Id,
            CustomerName = name,
            CustomerEmail = email,
            BookingTimeUtc = now,
            TotalAmount = total,
            Status = BookingStatus.Confirmed,
            Seats = seats.Select(s => new BookingSeat
            {
                SeatId = s.Id,
                ShowtimeId = showtime.Id,
                PriceAtBooking = showtime.BasePrice,
            }).ToList(),
            Payment = new Payment
            {
                Amount = total,
                PaymentMethod = "VNPAY",
                Status = PaymentStatus.Succeeded,
                TransactionId = $"SEED-{number:00}",
                CreatedUtc = now,
            },
        };
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
