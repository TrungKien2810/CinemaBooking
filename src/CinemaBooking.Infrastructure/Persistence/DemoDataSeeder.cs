using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CinemaBooking.Infrastructure.Persistence;

/// <summary>
/// Dữ liệu mẫu PHONG PHÚ cho demo: thêm phim, chi nhánh (đủ loại phòng/sơ đồ ghế), khách hàng, lịch chiếu
/// ~4 tuần (14 ngày trước → 13 ngày sau) và vài nghìn đơn đặt vé có thanh toán/soát vé để các trang doanh thu,
/// tổng quan, đơn đặt vé có số liệu thật.
/// <para>
/// Chạy được nhiều lần: phim theo tên, chi nhánh theo tên, tài khoản theo email, suất chiếu chỉ tạo cho (phòng, ngày)
/// còn trống, và đơn đặt vé chỉ sinh cho các suất vừa tạo trong lần chạy này. Dữ liệu sinh bằng bộ ngẫu nhiên cố định
/// (cùng một seed) nên mọi máy trong nhóm ra gần như giống nhau.
/// </para>
/// </summary>
public static class DemoDataSeeder
{
    public const string CustomerPassword = "Khach@12345";
    public const string BranchPassword = "Branch@12345";

    private const int DaysBack = 14;
    private const int DaysAhead = 13;
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly TimeSpan CleaningBuffer = TimeSpan.FromMinutes(15);

    private sealed record MovieSpec(
        string Title, string Genre, int Minutes, int DaysSinceRelease, string Poster, AgeRating Age,
        string Director, string Cast, string Description, bool Foreign, bool Family = false);

    private sealed record RoomSpec(string Name, int Rows, int Columns, RoomType Type, SeatLayoutPreset Preset, int Aisle);

    private sealed record TheaterSpec(
        string Name, string City, string Address, decimal BasePrice, string Slug, RoomSpec[] Rooms);

    // Ngày khởi chiếu tính lùi từ hôm nay để demo luôn có phim mới/hot/cũ và vài phim "sắp chiếu" (số âm).
    private static readonly MovieSpec[] Movies =
    [
        new("Mai", "Tình cảm", 131, 20, "/img/posters/mai.svg", AgeRating.T18, "Trấn Thành",
            "Phương Anh Đào, Tuấn Trần, Hồng Đào", "Cuộc đời một người phụ nữ nhiều thăng trầm và mối tình muộn màng.", false),
        new("Lật Mặt 7: Một Điều Ước", "Gia đình", 138, 35, "/img/posters/lat-mat-7.svg", AgeRating.T13, "Lý Hải",
            "Thanh Thức, Trương Minh Cường, Đình Toàn", "Một gia đình cùng nhau đi qua biến cố, và điều ước giản dị.", false),
        new("Deadpool & Wolverine", "Hành động", 128, 12, "/img/posters/deadpool-wolverine.svg", AgeRating.T18, "Shawn Levy",
            "Ryan Reynolds, Hugh Jackman, Emma Corrin", "Hai kẻ ngược đời buộc phải bắt tay trong cuộc phiêu lưu hỗn loạn.", true),
        new("Kung Fu Panda 4", "Hoạt hình", 94, 60, "/img/posters/kung-fu-panda-4.svg", AgeRating.P, "Mike Mitchell",
            "Jack Black, Awkwafina, Viola Davis", "Po phải chọn người kế nhiệm Thần Long Đại Hiệp.", true, Family: true),
        new("Godzilla x Kong: Đế Chế Mới", "Hành động", 115, 45, "/img/posters/godzilla-x-kong.svg", AgeRating.T13, "Adam Wingard",
            "Rebecca Hall, Brian Tyree Henry, Dan Stevens", "Hai quái vật huyền thoại cùng đối đầu mối đe dọa mới.", true),
        new("Oppenheimer", "Tiểu sử", 180, 90, "/img/posters/oppenheimer.svg", AgeRating.T18, "Christopher Nolan",
            "Cillian Murphy, Emily Blunt, Matt Damon", "Câu chuyện về cha đẻ của bom nguyên tử.", true),
        new("Cám", "Kinh dị", 122, 25, "/img/posters/cam.svg", AgeRating.T18, "Trần Hữu Tấn",
            "Quốc Huy, Thúy Diễm, Hạo Khang", "Phiên bản kinh dị đẫm máu của truyện cổ tích Tấm Cám.", false),
        new("Đào, Phở và Piano", "Chiến tranh", 100, 70, "/img/posters/dao-pho-va-piano.svg", AgeRating.T13, "Phi Tiến Sơn",
            "Doãn Quốc Đam, Cao Thị Hải Yến, NSND Trọng Trinh", "Hà Nội những ngày cuối năm 1946 và bản nhạc piano.", false),
        new("Wonka", "Gia đình", 116, 100, "/img/posters/wonka.svg", AgeRating.P, "Paul King",
            "Timothée Chalamet, Calah Lane, Olivia Colman", "Hành trình trở thành nhà làm kẹo sô-cô-la vĩ đại nhất.", true, Family: true),
        new("Spider-Man: Across the Spider-Verse", "Hoạt hình", 140, 150, "/img/posters/spider-verse.svg", AgeRating.K, "Joaquim Dos Santos",
            "Shameik Moore, Hailee Steinfeld, Oscar Isaac", "Miles Morales du hành qua đa vũ trụ Người Nhện.", true, Family: true),
        new("Tiếng Gọi Của Đại Dương", "Hoạt hình", 105, -9, "/img/posters/tieng-goi-dai-duong.svg", AgeRating.P, "Nguyễn Hải Yến",
            "Lồng tiếng: Hà Linh, Anh Tú, Mỹ Linh", "Cô bé vùng biển và hành trình tìm lại tiếng hát của đại dương.", false, Family: true),
        new("Mùa Hè Rực Rỡ", "Tình cảm", 112, -23, "/img/posters/mua-he-ruc-ro.svg", AgeRating.T13, "Victor Vũ",
            "Khả Ngân, Will, Jun Vũ", "Một mùa hè, ba người bạn và những điều chưa kịp nói.", false),
    ];

    private static readonly TheaterSpec[] NewTheaters =
    [
        new("CineSmart Bình Thạnh", "TP. Hồ Chí Minh", "25 Điện Biên Phủ, Quận Bình Thạnh", 90_000m, "binhthanh",
        [
            new("Phòng 1", 8, 12, RoomType.Standard, SeatLayoutPreset.Classic, 6),
            new("Phòng 2 (3D)", 7, 10, RoomType.ThreeD, SeatLayoutPreset.Auto, 0),
            new("Phòng Lounge", 5, 8, RoomType.Standard, SeatLayoutPreset.AllVip, 0),
        ]),
        new("CineSmart Long Biên", "Hà Nội", "27 Cổ Linh, Long Biên", 95_000m, "longbien",
        [
            new("Phòng IMAX", 12, 19, RoomType.Imax, SeatLayoutPreset.Auto, 9),
            new("Phòng 2", 8, 12, RoomType.Standard, SeatLayoutPreset.Classic, 6),
            new("Phòng 3", 6, 10, RoomType.Standard, SeatLayoutPreset.AllStandard, 0),
        ]),
        new("CineSmart Đà Nẵng", "Đà Nẵng", "910A Ngô Quyền, Sơn Trà", 85_000m, "danang",
        [
            new("Phòng 1", 9, 15, RoomType.Standard, SeatLayoutPreset.Classic, 7),
            new("Phòng 4DX", 6, 10, RoomType.FourDx, SeatLayoutPreset.Auto, 0),
        ]),
        new("CineSmart Cần Thơ", "Cần Thơ", "209 Đường 30/4, Ninh Kiều", 75_000m, "cantho",
        [
            new("Phòng 1", 8, 12, RoomType.Standard, SeatLayoutPreset.Classic, 0),
            new("Phòng 2", 6, 10, RoomType.Standard, SeatLayoutPreset.CoupleBack, 0),
        ]),
        new("CineSmart Hải Phòng", "Hải Phòng", "1 Võ Nguyên Giáp, Lê Chân", 80_000m, "haiphong",
        [
            new("Phòng 1", 8, 12, RoomType.ThreeD, SeatLayoutPreset.Auto, 6),
            new("Phòng 2", 6, 10, RoomType.Standard, SeatLayoutPreset.AllStandard, 0),
        ]),
    ];

    private static readonly string[] LastNames = ["Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng", "Bùi", "Đỗ", "Hồ", "Ngô", "Dương", "Lý"];
    private static readonly string[] MiddleNames = ["Văn", "Thị", "Minh", "Quốc", "Ngọc", "Thanh", "Hoàng", "Gia", "Đức", "Anh", "Hữu", "Thu"];
    private static readonly string[] FirstNames = ["An", "Bình", "Cường", "Dũng", "Giang", "Hà", "Hải", "Hương", "Khoa", "Lan", "Linh", "Long", "Mai", "Nam", "Phúc", "Quân", "Thảo", "Trang", "Tuấn", "Vy", "Yến", "Nhi", "Huy", "Phương"];

    private sealed record Customer(string? UserId, string Name, string Email);

    public static async Task SeedAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        var db = sp.GetRequiredService<CinemaDbContext>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoDataSeeder));
        var rng = new Random(20_261_106);
        var started = DateTime.UtcNow;

        var movies = await EnsureMoviesAsync(db, ct);
        var theaters = await EnsureTheatersAsync(db, ct);
        await CatalogSeeder.EnsurePricingAsync(db, name => NewTheaters.FirstOrDefault(t => t.Name == name)?.BasePrice ?? BasePriceOf(name), ct);
        await EnsurePromotionsAsync(db, theaters, ct);
        await EnsureVouchersAsync(db, theaters, ct);
        await EnsureBranchAdminsAsync(sp, db, theaters, log);
        var customers = await EnsureCustomersAsync(sp, rng, log);

        var pricings = await db.TheaterPricings.AsNoTracking().ToDictionaryAsync(p => p.TheaterId, ct);
        var created = await EnsureShowtimesAsync(db, movies, theaters, pricings, rng, ct);
        var bookings = await GenerateBookingsAsync(db, created, movies, theaters, pricings, customers, rng, ct);

        if (created.Count > 0 || bookings > 0)
            log.LogInformation("Dữ liệu demo: thêm {Showtimes} suất chiếu, {Bookings} đơn đặt vé trong {Seconds:0.0}s.",
                created.Count, bookings, (DateTime.UtcNow - started).TotalSeconds);
    }

    // ── Phim ──────────────────────────────────────────────────────────────────

    private static async Task<List<Movie>> EnsureMoviesAsync(CinemaDbContext db, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow + VietnamOffset);
        var existing = await db.Movies.ToListAsync(ct);
        var titles = existing.Select(m => m.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in Movies.Where(m => !titles.Contains(m.Title)))
        {
            db.Movies.Add(new Movie
            {
                Title = spec.Title, Genre = spec.Genre, DurationMinutes = spec.Minutes,
                ReleaseDate = today.AddDays(-spec.DaysSinceRelease), PosterUrl = spec.Poster, AgeRating = spec.Age,
                Director = spec.Director, Cast = spec.Cast, Description = spec.Description,
                TrailerUrl = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(spec.Title + " trailer"),
            });
        }
        await db.SaveChangesAsync(ct);
        return await db.Movies.Where(m => m.IsActive).ToListAsync(ct);
    }

    // ── Chi nhánh, phòng, ghế ─────────────────────────────────────────────────

    private static async Task<List<Theater>> EnsureTheatersAsync(CinemaDbContext db, CancellationToken ct)
    {
        var names = (await db.Theaters.Select(t => t.Name).ToListAsync(ct)).ToHashSet();

        foreach (var spec in NewTheaters.Where(t => !names.Contains(t.Name)))
        {
            var theater = new Theater { Name = spec.Name, City = spec.City, Address = spec.Address };
            foreach (var r in spec.Rooms)
                theater.Rooms.Add(new Room { RoomName = r.Name, TotalRows = r.Rows, TotalColumns = r.Columns, RoomType = r.Type });
            db.Theaters.Add(theater);
            await db.SaveChangesAsync(ct);

            foreach (var (room, r) in theater.Rooms.Zip(spec.Rooms))
                db.Seats.AddRange(SeatGenerator.Generate(room, r.Preset, r.Aisle));
            await db.SaveChangesAsync(ct);
        }

        // Thông tin liên hệ còn trống (DB cũ / chi nhánh mẫu gốc): điền số hotline, email và giờ mở cửa mặc định.
        var missing = await db.Theaters.Where(t => t.Phone == null && t.OpeningHours == null).ToListAsync(ct);
        foreach (var t in missing)
        {
            t.Phone = $"1900 {6000 + t.Id:0000}";
            t.Email = $"{Slugify(t.Name)}@cinesmart.vn";
            t.OpeningHours = "08:00 – 23:30 hằng ngày";
            t.Description ??= $"Cụm rạp {t.Name} tại {t.City}: phòng chiếu hiện đại, ghế êm, âm thanh sống động, bãi đỗ xe thuận tiện.";
        }
        if (missing.Count > 0) await db.SaveChangesAsync(ct);

        return await db.Theaters.Include(t => t.Rooms).ThenInclude(r => r.Seats).Where(t => t.IsActive)
            .AsSplitQuery().OrderBy(t => t.Id).ToListAsync(ct);
    }

    private static async Task EnsurePromotionsAsync(CinemaDbContext db, List<Theater> theaters, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow + VietnamOffset);
        var have = (await db.Promotions.Select(p => p.TheaterId).ToListAsync(ct)).ToHashSet();

        foreach (var theater in theaters.Where(t => !have.Contains(t.Id) && NewTheaters.Any(s => s.Name == t.Name)))
        {
            db.Promotions.Add(new Promotion
            {
                TheaterId = theater.Id, Title = "Thứ 4 vui vẻ", Badge = "-20%",
                Description = "Giảm 20% mọi ghế thường vào thứ 4 hằng tuần.",
                StartDate = today.AddDays(-7), EndDate = today.AddDays(90),
                DiscountPercent = 20, ApplicableSeatType = SeatType.Standard, DaysOfWeekMask = 1 << (int)DayOfWeek.Wednesday,
            });
            db.Promotions.Add(new Promotion
            {
                TheaterId = theater.Id, Title = "Suất sớm", Badge = "Đồng giá 55k",
                Description = "Đồng giá 55.000đ cho các suất bắt đầu trước 11:00 ngày thường.",
                StartDate = today.AddDays(-7), EndDate = today.AddDays(90),
                FixedPrice = 55_000m, DaysOfWeekMask = 0b0111110, ToTime = new TimeOnly(11, 0),
            });
        }
        await db.SaveChangesAsync(ct);
    }

    // ── Mã giảm giá mẫu ───────────────────────────────────────────────────────

    private static async Task EnsureVouchersAsync(CinemaDbContext db, List<Theater> theaters, CancellationToken ct)
    {
        if (await db.Vouchers.AnyAsync(ct)) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow + VietnamOffset);
        var longBien = theaters.FirstOrDefault(t => t.Name == "CineSmart Long Biên");

        db.Vouchers.AddRange(
            new Voucher
            {
                Code = "CINE10", Title = "Giảm 10% mọi suất", Description = "Giảm 10%, tối đa 30.000đ cho đơn từ 100.000đ.",
                DiscountPercent = 10, MaxDiscountAmount = 30_000m, MinOrderAmount = 100_000m,
                StartDate = today.AddDays(-30), EndDate = today.AddDays(120), PerCustomerLimit = 0,
            },
            new Voucher
            {
                Code = "WELCOME20", Title = "Chào bạn mới", Description = "Giảm 20.000đ cho đơn từ 150.000đ, mỗi khách dùng 1 lần.",
                DiscountAmount = 20_000m, MinOrderAmount = 150_000m,
                StartDate = today.AddDays(-30), EndDate = today.AddDays(180), PerCustomerLimit = 1,
            },
            new Voucher
            {
                Code = "COUPLE50", Title = "Đi đôi giảm sâu", Description = "Giảm 50.000đ cho đơn từ 300.000đ, 200 lượt đầu tiên.",
                DiscountAmount = 50_000m, MinOrderAmount = 300_000m, UsageLimit = 200,
                StartDate = today.AddDays(-7), EndDate = today.AddDays(60), PerCustomerLimit = 2,
            },
            new Voucher
            {
                Code = "LONGBIEN15", Title = "Long Biên khai trương", Description = "Giảm 15% (tối đa 40.000đ) chỉ tại CineSmart Long Biên.",
                TheaterId = longBien?.Id, DiscountPercent = 15, MaxDiscountAmount = 40_000m,
                StartDate = today.AddDays(-14), EndDate = today.AddDays(90), PerCustomerLimit = 3,
            },
            new Voucher
            {
                Code = "HETHAN", Title = "Mã đã hết hạn (để thử)", DiscountPercent = 50,
                StartDate = today.AddDays(-90), EndDate = today.AddDays(-30), PerCustomerLimit = 0,
            });
        await db.SaveChangesAsync(ct);
    }

    // ── Tài khoản ─────────────────────────────────────────────────────────────

    private static async Task EnsureBranchAdminsAsync(
        IServiceProvider sp, CinemaDbContext db, List<Theater> theaters, ILogger log)
    {
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var spec in NewTheaters)
        {
            var theater = theaters.FirstOrDefault(t => t.Name == spec.Name);
            if (theater is null) continue;

            var email = $"branch.{spec.Slug}@cinemabooking.local";
            if (await users.FindByEmailAsync(email) is not null) continue;

            var user = new ApplicationUser
            {
                UserName = email, Email = email, EmailConfirmed = true,
                FullName = $"Quản lý {spec.Name.Replace("CineSmart ", "")}", TheaterId = theater.Id,
            };
            var result = await users.CreateAsync(user, BranchPassword);
            if (!result.Succeeded) { log.LogWarning("Không tạo được {Email}: {Errors}", email, string.Join("; ", result.Errors.Select(e => e.Description))); continue; }
            await users.AddToRoleAsync(user, AppRoles.BranchAdmin);
        }
        _ = db;
    }

    private static async Task<List<Customer>> EnsureCustomersAsync(IServiceProvider sp, Random rng, ILogger log)
    {
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var list = new List<Customer>();

        for (var i = 1; i <= 20; i++)
        {
            var name = $"{Pick(rng, LastNames)} {Pick(rng, MiddleNames)} {Pick(rng, FirstNames)}";
            var email = $"khach{i:00}@example.com";
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = name, PhoneNumber = Phone(rng) };
                var result = await users.CreateAsync(user, CustomerPassword);
                if (!result.Succeeded) { log.LogWarning("Không tạo được {Email}", email); continue; }
                await users.AddToRoleAsync(user, AppRoles.Customer);
            }
            list.Add(new Customer(user.Id, user.FullName ?? name, email));
        }
        return list;
    }

    // ── Suất chiếu ────────────────────────────────────────────────────────────

    private static async Task<List<Showtime>> EnsureShowtimesAsync(
        CinemaDbContext db, List<Movie> movies, List<Theater> theaters, Dictionary<int, TheaterPricing> pricings,
        Random rng, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now + VietnamOffset);
        var fromUtc = today.AddDays(-DaysBack).ToDateTime(TimeOnly.MinValue) - VietnamOffset;
        var toUtc = today.AddDays(DaysAhead + 1).ToDateTime(TimeOnly.MinValue) - VietnamOffset;

        // (phòng, ngày giờ rạp) đã có suất → bỏ qua cả ngày đó.
        var taken = (await db.Showtimes.AsNoTracking()
                .Where(s => s.StartTimeUtc >= fromUtc.AddDays(-1) && s.StartTimeUtc < toUtc)
                .Select(s => new { s.RoomId, s.StartTimeUtc }).ToListAsync(ct))
            .Select(s => (s.RoomId, Day: DateOnly.FromDateTime(s.StartTimeUtc + VietnamOffset)))
            .ToHashSet();

        var created = new List<Showtime>();
        var specByTitle = Movies.ToDictionary(m => m.Title);
        var hotOrder = movies.OrderByDescending(m => m.ReleaseDate).ToList();

        foreach (var theater in theaters)
        {
            var policy = pricings.TryGetValue(theater.Id, out var found) ? found : PricingRules.DefaultFor(theater.Id, BasePriceOf(theater.Name));
            var rooms = theater.Rooms.OrderByDescending(r => r.Seats.Count).ToList();

            for (var d = -DaysBack; d <= DaysAhead; d++)
            {
                var day = today.AddDays(d);
                var available = hotOrder.Where(m => m.ReleaseDate <= day).ToList();
                if (available.Count == 0) continue;

                for (var ri = 0; ri < rooms.Count; ri++)
                {
                    var room = rooms[ri];
                    if (taken.Contains((room.Id, day))) continue;

                    var slot = 0;
                    var cursor = day.ToDateTime(new TimeOnly(9, 0)).AddMinutes(rng.Next(0, 4) * 5 + ri * 10);
                    Movie? previous = null;

                    while (cursor <= day.ToDateTime(new TimeOnly(23, 15)))
                    {
                        var prime = cursor.TimeOfDay >= TimeSpan.FromHours(18) && cursor.TimeOfDay < TimeSpan.FromHours(22);
                        // Giờ vàng: phòng lớn nhất ↔ phim mới nhất; ngoài giờ vàng xoay vòng các phim.
                        var pool = prime ? available.Take(Math.Max(3, rooms.Count + 1)).ToList() : available;
                        var movie = pool[(prime ? ri + slot : d + 7 + ri * 3 + slot * 2 + 1000) % pool.Count];
                        if (ReferenceEquals(movie, previous) && pool.Count > 1) movie = pool[(pool.IndexOf(movie) + 1) % pool.Count];

                        var startUtc = DateTime.SpecifyKind(cursor - VietnamOffset, DateTimeKind.Utc);
                        specByTitle.TryGetValue(movie.Title, out var spec);
                        // Phim mẫu gốc (CatalogSeeder) không nằm trong danh sách trên: nhận diện phim ngoại theo tên.
                        var foreign = spec?.Foreign ?? movie.Title is "Dune: Part Two" or "Inside Out 2";
                        var family = spec?.Family ?? movie.Title == "Inside Out 2";

                        // Giá theo BẢNG GIÁ chi nhánh (loại phòng + giờ + ngày) — không phụ thuộc phim.
                        var price = PricingRules.BasePrice(policy, room.RoomType, cursor);

                        var language = foreign
                            ? (family && slot % 2 == 0 ? ShowLanguage.Dubbed : ShowLanguage.Subtitled)
                            : ShowLanguage.Original;

                        var showtime = new Showtime
                        {
                            MovieId = movie.Id, RoomId = room.Id, StartTimeUtc = startUtc,
                            EndTimeUtc = startUtc.AddMinutes(movie.DurationMinutes),
                            BasePrice = price, Language = language,
                        };
                        created.Add(showtime);

                        previous = movie;
                        slot++;
                        cursor = RoundUp5(cursor.AddMinutes(movie.DurationMinutes) + CleaningBuffer);
                    }
                }
            }
        }

        // Lưu theo lô để không giữ cả nghìn entity trong một lần SaveChanges.
        foreach (var chunk in created.Chunk(500))
        {
            db.Showtimes.AddRange(chunk);
            await db.SaveChangesAsync(ct);
        }
        db.ChangeTracker.Clear();
        return created;
    }

    private static decimal BasePriceOf(string theaterName) => theaterName switch
    {
        "CineSmart Nguyễn Huệ" => 95_000m,
        "CineSmart Thảo Điền" => 85_000m,
        "CineSmart Cầu Giấy" => 90_000m,
        "CineSmart Hoàn Kiếm" => 80_000m,
        _ => 85_000m,
    };

    // ── Đơn đặt vé ────────────────────────────────────────────────────────────

    private sealed record RoomLayout(
        Dictionary<int, List<Seat>> Rows, List<(Seat Left, Seat Right)> CouplePairs, int Capacity);

    private static RoomLayout BuildLayout(Room room)
    {
        var rows = room.Seats.GroupBy(s => s.RowNumber)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.ColumnNumber).ToList());

        var pairs = new List<(Seat, Seat)>();
        foreach (var row in rows.Values)
        {
            var couple = row.Where(s => s.SeatType == SeatType.Couple).ToList();
            for (var i = 0; i + 1 < couple.Count;)
            {
                if (couple[i].ColumnNumber + 1 == couple[i + 1].ColumnNumber) { pairs.Add((couple[i], couple[i + 1])); i += 2; }
                else i++;
            }
        }
        return new RoomLayout(rows, pairs, room.Seats.Count);
    }

    private static async Task<int> GenerateBookingsAsync(
        CinemaDbContext db, List<Showtime> showtimes, List<Movie> movies, List<Theater> theaters,
        Dictionary<int, TheaterPricing> pricings, List<Customer> customers, Random rng, CancellationToken ct)
    {
        if (showtimes.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var codes = new HashSet<string>(await db.Bookings.Select(b => b.BookingCode).ToListAsync(ct));
        var generator = new BookingCodeGenerator();
        var rooms = theaters.SelectMany(t => t.Rooms).ToDictionary(r => r.Id);
        var layouts = rooms.ToDictionary(kv => kv.Key, kv => BuildLayout(kv.Value));
        var hotIds = movies.OrderByDescending(m => m.ReleaseDate).Take(4).Select(m => m.Id).ToHashSet();
        var batch = new List<Booking>();
        var total = 0;
        var transaction = 0;

        foreach (var showtime in showtimes)
        {
            var past = showtime.StartTimeUtc < now;
            var hoursAhead = (showtime.StartTimeUtc - now).TotalHours;
            if (!past && hoursAhead > 96) continue; // xa quá thì chưa ai đặt

            var localStart = showtime.StartTimeUtc + VietnamOffset;
            var prime = localStart.TimeOfDay >= TimeSpan.FromHours(18) && localStart.TimeOfDay < TimeSpan.FromHours(22);
            var weekend = localStart.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var room = rooms[showtime.RoomId];
            var layout = layouts[room.Id];

            var occupancy = 0.10 + rng.NextDouble() * 0.32
                + (prime ? 0.18 : 0) + (weekend ? 0.10 : 0) + (hotIds.Contains(showtime.MovieId) ? 0.14 : 0)
                + (room.RoomType == RoomType.Imax ? 0.08 : 0);
            if (!past) occupancy *= hoursAhead < 24 ? 0.65 : hoursAhead < 48 ? 0.45 : 0.25;
            occupancy = Math.Clamp(occupancy * 0.7, 0.03, 0.85);

            var target = (int)Math.Round(layout.Capacity * occupancy);
            var taken = new HashSet<int>();
            var guard = 0;

            while (taken.Count < target && guard++ < 200)
            {
                var seats = PickGroup(layout, taken, rng);
                if (seats.Count == 0) continue;
                foreach (var s in seats) taken.Add(s.Id);

                var customer = rng.NextDouble() < 0.45 ? customers[rng.Next(customers.Count)] : RandomGuest(rng);
                var bookedAt = past
                    ? showtime.StartTimeUtc.AddHours(-(0.4 + rng.NextDouble() * 70))
                    : now.AddMinutes(-rng.Next(5, (int)Math.Max(10, Math.Min(hoursAhead, 60) * 60)));
                if (bookedAt > now) bookedAt = now.AddMinutes(-5);

                var code = generator.Next();
                while (!codes.Add(code)) code = generator.Next();

                var surcharges = pricings.TryGetValue(room.TheaterId, out var pol) ? PricingRules.Surcharges(pol) : null;
                var lines = seats.Select(s => (Seat: s, Price: PriceOf(showtime.BasePrice, s.SeatType, surcharges))).ToList();
                var amount = lines.Sum(l => l.Price);
                var checkedIn = past && rng.NextDouble() < 0.9;

                batch.Add(new Booking
                {
                    BookingCode = code, UserId = customer.UserId, ShowtimeId = showtime.Id,
                    CustomerName = customer.Name, CustomerEmail = customer.Email, CustomerPhone = Phone(rng),
                    BookingTimeUtc = bookedAt, TotalAmount = amount, Status = BookingStatus.Confirmed,
                    CheckedInUtc = checkedIn ? showtime.StartTimeUtc.AddMinutes(-rng.Next(4, 40)) : null,
                    CheckedInBy = checkedIn ? "staff@cinemabooking.local" : null,
                    Seats = lines.Select(l => new BookingSeat
                    {
                        SeatId = l.Seat.Id, ShowtimeId = showtime.Id, PriceAtBooking = l.Price,
                    }).ToList(),
                    Payment = new Payment
                    {
                        Amount = amount, PaymentMethod = "VNPAY", Status = PaymentStatus.Succeeded,
                        TransactionId = $"DEMO{++transaction:000000}", CreatedUtc = bookedAt, PaidUtc = bookedAt.AddSeconds(90),
                    },
                });
            }

            // Vài đơn hủy/hết hạn (không giữ ghế) để danh sách đơn có đủ trạng thái.
            if (past && rng.NextDouble() < 0.18)
            {
                var customer = RandomGuest(rng);
                var code = generator.Next();
                while (!codes.Add(code)) code = generator.Next();
                var expired = rng.NextDouble() < 0.5;
                batch.Add(new Booking
                {
                    BookingCode = code, ShowtimeId = showtime.Id, CustomerName = customer.Name, CustomerEmail = customer.Email,
                    CustomerPhone = Phone(rng), BookingTimeUtc = showtime.StartTimeUtc.AddHours(-rng.Next(2, 30)),
                    TotalAmount = showtime.BasePrice * 2, Status = expired ? BookingStatus.Expired : BookingStatus.Cancelled,
                    Payment = new Payment
                    {
                        Amount = showtime.BasePrice * 2, PaymentMethod = "VNPAY", Status = PaymentStatus.Failed,
                        CreatedUtc = showtime.StartTimeUtc.AddHours(-rng.Next(2, 30)),
                    },
                });
            }

            if (batch.Count >= 400)
            {
                total += batch.Count;
                db.Bookings.AddRange(batch);
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            total += batch.Count;
            db.Bookings.AddRange(batch);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return total;
    }

    /// <summary>Một nhóm ghế liền kề cùng hàng (1–4 ghế thường/VIP) hoặc một cặp ghế đôi; rỗng nếu không tìm được chỗ.</summary>
    private static List<Seat> PickGroup(RoomLayout layout, HashSet<int> taken, Random rng)
    {
        if (layout.CouplePairs.Count > 0 && rng.NextDouble() < 0.12)
        {
            var (left, right) = layout.CouplePairs[rng.Next(layout.CouplePairs.Count)];
            return taken.Contains(left.Id) || taken.Contains(right.Id) ? [] : [left, right];
        }

        var size = rng.NextDouble() switch { < 0.22 => 1, < 0.68 => 2, < 0.82 => 3, _ => 4 };
        var rowKeys = layout.Rows.Keys.ToList();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var row = layout.Rows[rowKeys[rng.Next(rowKeys.Count)]];
            var start = rng.Next(0, Math.Max(1, row.Count - size + 1));
            var run = row.Skip(start).Take(size).ToList();
            var contiguous = run.Count == size
                && run.Zip(run.Skip(1), (a, b) => b.ColumnNumber == a.ColumnNumber + 1).All(x => x)
                && run.All(s => s.SeatType != SeatType.Couple && !taken.Contains(s.Id));
            if (contiguous) return run;
        }
        return [];
    }

    private static decimal PriceOf(decimal basePrice, SeatType type, SeatSurcharges? surcharges) =>
        surcharges is null ? (type == SeatType.Vip ? Math.Round(basePrice * 1.2m, 0) : basePrice) : basePrice + surcharges.For(type);

    private static Customer RandomGuest(Random rng)
    {
        var name = $"{Pick(rng, LastNames)} {Pick(rng, MiddleNames)} {Pick(rng, FirstNames)}";
        var slug = RemoveDiacritics(name).ToLowerInvariant().Replace(' ', '.');
        return new Customer(null, name, $"{slug}{rng.Next(10, 99)}@example.com");
    }

    private static string Slugify(string text) =>
        RemoveDiacritics(text).ToLowerInvariant().Replace("cinesmart ", "").Replace(' ', '-');

    private static string Phone(Random rng) => "09" + rng.Next(10_000_000, 99_999_999);

    private static string Pick(Random rng, string[] items) => items[rng.Next(items.Length)];

    private static DateTime RoundUp5(DateTime t)
    {
        var rem = (int)(t.TimeOfDay.TotalMinutes % 5);
        var rounded = rem == 0 ? t : t.AddMinutes(5 - rem);
        return new DateTime(rounded.Year, rounded.Month, rounded.Day, rounded.Hour, rounded.Minute, 0);
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Replace('Đ', 'D').Replace('đ', 'd').Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in normalized)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}
