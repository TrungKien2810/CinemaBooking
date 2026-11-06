using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>
/// Xếp lịch chiếu cho từng chi nhánh: chọn phim từ danh mục chung + một phòng của chi nhánh,
/// đặt giá riêng cho suất (General.md §3.2.1).
/// </summary>
public class ShowtimesController : AdminControllerBase
{
    /// <summary>Thời gian dọn phòng tối thiểu giữa hai suất liên tiếp trong cùng một phòng.</summary>
    public static readonly TimeSpan CleaningBuffer = TimeSpan.FromMinutes(15);

    private readonly CinemaDbContext _db;
    private readonly IShowtimeScheduler _scheduler;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;
    private readonly IBranchScope _branches;

    public ShowtimesController(
        CinemaDbContext db,
        IShowtimeScheduler scheduler,
        IOptions<DisplayTimeZone> tz,
        TimeProvider clock,
        IBranchScope branches)
    {
        _db = db;
        _scheduler = scheduler;
        _tz = tz.Value;
        _clock = clock;
        _branches = branches;
    }

    /// <summary>Giữ chữ ký cũ (theaterId, date) cho code gọi trực tiếp; MVC chỉ nhận action có đủ bộ lọc bên dưới.</summary>
    [NonAction]
    public Task<IActionResult> Index(int? theaterId, DateOnly? date, CancellationToken ct) =>
        Index(theaterId, date, null, null, ct);

    /// <param name="movieId">Chỉ hiện suất của phim này.</param>
    /// <param name="status">"active" = đang bán, "hidden" = đang ẩn.</param>
    public async Task<IActionResult> Index(int? theaterId, DateOnly? date, int? movieId, string? status, CancellationToken ct)
    {
        var day = date ?? _tz.ToLocalDate(_clock.GetUtcNow().UtcDateTime);
        var (fromUtc, toUtc) = _tz.DayRangeUtc(day);

        // BranchAdmin chỉ thấy suất của chi nhánh mình; sửa URL sang chi nhánh khác thì chặn.
        var scoped = await _branches.TheaterIdAsync(ct);
        if (scoped is not null)
        {
            if (theaterId is not null && theaterId != scoped) return Forbid();
            theaterId = scoped;
        }

        var query = _db.Showtimes.AsNoTracking()
            .Where(s => s.StartTimeUtc >= fromUtc && s.StartTimeUtc < toUtc);
        if (theaterId is not null)
            query = query.Where(s => s.Room!.TheaterId == theaterId);
        if (movieId is not null) query = query.Where(s => s.MovieId == movieId);
        var statusFilter = status is "active" or "hidden" ? status : null;
        if (statusFilter is not null) query = query.Where(s => s.IsActive == (statusFilter == "active"));

        var rows = await query
            .OrderBy(s => s.Room!.Theater!.Name)
            .ThenBy(s => s.StartTimeUtc)
            .Select(s => new
            {
                s.Id,
                s.Movie!.Title,
                TheaterName = s.Room!.Theater!.Name,
                s.Room.RoomName,
                s.StartTimeUtc,
                s.EndTimeUtc,
                s.BasePrice,
                s.IsActive,
                s.Language,
                s.Room.RoomType,
                Bookings = _db.Bookings.Count(b => b.ShowtimeId == s.Id),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new ShowtimeAdminItem(
                r.Id, r.Title, r.TheaterName, r.RoomName,
                _tz.ToLocalTime(r.StartTimeUtc), _tz.ToLocalTime(r.EndTimeUtc), r.BasePrice, r.Bookings, r.IsActive, r.Language, r.RoomType))
            .ToList();

        var theatersQuery = _db.Theaters.AsNoTracking().Where(t => t.IsActive);
        if (scoped is not null) theatersQuery = theatersQuery.Where(t => t.Id == scoped);

        var theaters = await theatersQuery
            .OrderBy(t => t.City).ThenBy(t => t.Name)
            .Select(t => new SelectListItem(t.City + " · " + t.Name, t.Id.ToString()))
            .ToListAsync(ct);

        var movieOptions = await _db.Movies.AsNoTracking().Where(m => m.IsActive)
            .OrderBy(m => m.Title)
            .Select(m => new SelectListItem(m.Title, m.Id.ToString()))
            .ToListAsync(ct);

        return View(new ShowtimesIndexViewModel(day, theaterId, theaters, items, movieId, statusFilter, movieOptions));
    }

    /// <summary>
    /// Gợi ý giờ bắt đầu còn trống cho (phòng, phim, ngày) — form thêm suất gọi bằng fetch để chủ chi nhánh
    /// bấm chọn thay vì tự gõ ngày giờ. Phạm vi chi nhánh kiểm tra như mọi action khác.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Suggest(int roomId, int movieId, DateOnly date, int? excludeId, CancellationToken ct)
    {
        var theaterId = await _db.Rooms.Where(r => r.Id == roomId).Select(r => (int?)r.TheaterId).FirstOrDefaultAsync(ct);
        if (theaterId is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(theaterId.Value, ct)) return Forbid();

        var duration = await _db.Movies.AsNoTracking().Where(m => m.Id == movieId && m.IsActive)
            .Select(m => (int?)m.DurationMinutes).FirstOrDefaultAsync(ct);
        if (duration is null) return NotFound();

        // Lấy dư nửa ngày hai đầu để thấy suất qua đêm từ hôm trước.
        var (fromUtc, toUtc) = _tz.DayRangeUtc(date);
        var busy = (await _db.Showtimes.AsNoTracking()
                .Where(s => s.RoomId == roomId && s.Id != excludeId
                            && s.StartTimeUtc < toUtc.AddHours(12) && s.EndTimeUtc > fromUtc.AddHours(-12))
                .Select(s => new { s.StartTimeUtc, s.EndTimeUtc })
                .ToListAsync(ct))
            .Select(s => (_tz.ToLocalTime(s.StartTimeUtc), _tz.ToLocalTime(s.EndTimeUtc)));

        var nowLocal = _tz.ToLocalTime(_clock.GetUtcNow().UtcDateTime);
        var slots = ShowtimeSlotSuggester.Suggest(date, duration.Value, busy, CleaningBuffer, nowLocal);

        return Json(slots.Select(s => new
        {
            value = s.StartLocal.ToString("yyyy-MM-ddTHH:mm"),
            text = $"{s.StartLocal:HH:mm} – {s.EndLocal:HH:mm}",
            snug = s.Snug,
        }));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var form = new ShowtimeFormModel();
        await FillOptionsAsync(form, ct);
        return View("Form", form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ShowtimeFormModel form, CancellationToken ct)
    {
        var showtime = new Showtime();
        if (!await TryApplyAsync(form, showtime, ct))
        {
            await FillOptionsAsync(form, ct);
            return View("Form", form);
        }

        _db.Showtimes.Add(showtime);
        await _db.SaveChangesAsync(ct);

        return RedirectToIndexOf(showtime);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var showtime = await _db.Showtimes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (showtime is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(await TheaterIdOfRoomAsync(showtime.RoomId, ct), ct))
            return Forbid();

        var form = new ShowtimeFormModel
        {
            Id = showtime.Id,
            MovieId = showtime.MovieId,
            RoomId = showtime.RoomId,
            StartTimeLocal = _tz.ToLocalTime(showtime.StartTimeUtc),
            BasePrice = showtime.BasePrice,
            Language = showtime.Language,
        };
        await FillOptionsAsync(form, ct);
        return View("Form", form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ShowtimeFormModel form, CancellationToken ct)
    {
        var showtime = await _db.Showtimes.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (showtime is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(await TheaterIdOfRoomAsync(showtime.RoomId, ct), ct))
            return Forbid();

        form.Id = id;

        // Vé đã đặt giữ SeatId của phòng hiện tại — đổi phòng thì các vé đó trỏ vào ghế của phòng khác.
        if (form.RoomId != showtime.RoomId && await _db.Bookings.AnyAsync(b => b.ShowtimeId == id, ct))
            ModelState.AddModelError(nameof(form.RoomId), "Suất đã có vé được đặt, không đổi phòng được.");

        if (!await TryApplyAsync(form, showtime, ct))
        {
            await FillOptionsAsync(form, ct);
            return View("Form", form);
        }

        await _db.SaveChangesAsync(ct);
        return RedirectToIndexOf(showtime);
    }

    /// <summary>Tạm ẩn / hiện lại suất: suất ẩn không bán cho khách, đơn đã đặt vẫn giữ nguyên.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, CancellationToken ct)
    {
        var showtime = await _db.Showtimes.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (showtime is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(await TheaterIdOfRoomAsync(showtime.RoomId, ct), ct))
            return Forbid();

        showtime.IsActive = !showtime.IsActive;
        await _db.SaveChangesAsync(ct);

        TempData["Message"] = showtime.IsActive ? "Đã hiện lại suất chiếu." : "Đã tạm ẩn suất chiếu — khách không đặt được nữa.";
        return RedirectToIndexOf(showtime);
    }

    /// <summary>
    /// Sao chép lịch chiếu của một ngày sang ngày khác (cùng giờ, phim, phòng, giá). Suất trùng lịch
    /// hoặc ở quá khứ bị bỏ qua và được đếm lại để báo cho người dùng.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyDay(DateOnly sourceDate, DateOnly targetDate, int? theaterId, CancellationToken ct)
    {
        var scoped = await _branches.TheaterIdAsync(ct);
        if (scoped is not null)
        {
            if (theaterId is not null && theaterId != scoped) return Forbid();
            theaterId = scoped;
        }
        else if (theaterId is not null && !await _branches.CanAccessTheaterAsync(theaterId.Value, ct))
        {
            return Forbid();
        }

        var back = RedirectToAction(nameof(Index), new { date = targetDate.ToString("yyyy-MM-dd"), theaterId });
        if (sourceDate == targetDate)
        {
            TempData["Error"] = "Ngày đích phải khác ngày nguồn.";
            return RedirectToAction(nameof(Index), new { date = sourceDate.ToString("yyyy-MM-dd"), theaterId });
        }

        var (fromUtc, toUtc) = _tz.DayRangeUtc(sourceDate);
        var query = _db.Showtimes.AsNoTracking()
            .Where(s => s.StartTimeUtc >= fromUtc && s.StartTimeUtc < toUtc && s.Movie!.IsActive && s.Room!.Theater!.IsActive);
        if (theaterId is not null) query = query.Where(s => s.Room!.TheaterId == theaterId);

        var sources = await query.OrderBy(s => s.StartTimeUtc).ToListAsync(ct);
        var shift = TimeSpan.FromDays(targetDate.DayNumber - sourceDate.DayNumber);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        int copied = 0, skipped = 0;

        var pricings = new Dictionary<int, TheaterPricing?>();
        var roomInfo = await _db.Rooms.AsNoTracking().ToDictionaryAsync(r => r.Id, r => (r.TheaterId, r.RoomType), ct);

        foreach (var src in sources)
        {
            var candidate = new Showtime { MovieId = src.MovieId, StartTimeUtc = src.StartTimeUtc + shift };
            await _scheduler.PrepareAsync(candidate, ct);

            if (candidate.StartTimeUtc < nowUtc
                || await FindOverlapAsync(src.RoomId, candidate.StartTimeUtc, candidate.EndTimeUtc, null, ct) is not null)
            {
                skipped++;
                continue;
            }

            candidate.RoomId = src.RoomId;
            // Giá theo bảng giá chi nhánh cho ngày/giờ mới (chiếu cuối tuần có thể khác ngày thường); chưa có bảng giá thì giữ giá cũ.
            candidate.BasePrice = src.BasePrice;
            if (roomInfo.TryGetValue(src.RoomId, out var ri))
            {
                if (!pricings.TryGetValue(ri.TheaterId, out var pr))
                    pricings[ri.TheaterId] = pr = await PricingLoader.LoadAsync(_db, ri.TheaterId, ct);
                if (pr is not null)
                    candidate.BasePrice = PricingRules.BasePrice(pr, ri.RoomType, _tz.ToLocalTime(candidate.StartTimeUtc));
            }
            candidate.Language = src.Language;
            _db.Showtimes.Add(candidate);
            await _db.SaveChangesAsync(ct); // lưu từng suất để suất sau thấy suất trước khi kiểm tra trùng
            copied++;
        }

        TempData["Message"] = $"Đã sao chép {copied} suất sang {targetDate:dd/MM/yyyy}" +
                              (skipped > 0 ? $"; bỏ qua {skipped} suất (trùng lịch hoặc đã qua giờ)." : ".");
        return back;
    }

    // ── Gợi ý lịch chiếu cả ngày ───────────────────────────────────────────────

    /// <summary>
    /// Trang gợi ý lịch cả ngày cho một chi nhánh: chọn ngày + phim + giá, xem trước lịch do
    /// <see cref="ShowtimeAutoPlanner"/> đề xuất (phim hot → phòng lớn, giờ vàng), rồi mới áp dụng.
    /// BranchAdmin chỉ làm được cho chi nhánh mình.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> AutoPlan(int? theaterId, DateOnly? date, CancellationToken ct)
    {
        var resolved = await ResolveTheaterAsync(theaterId, ct);
        if (resolved.Result is not null) return resolved.Result;

        var today = _tz.ToLocalDate(_clock.GetUtcNow().UtcDateTime);
        var form = new AutoPlanForm { TheaterId = resolved.TheaterId, Date = date ?? today.AddDays(1) };
        form.BasePrice = await SuggestBasePriceAsync(resolved.TheaterId!.Value, form.BasePrice, ct);

        return View(await BuildViewAsync(form, preview: false, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AutoPlan(AutoPlanForm form, string? command, CancellationToken ct)
    {
        var resolved = await ResolveTheaterAsync(form.TheaterId, ct);
        if (resolved.Result is not null) return resolved.Result;
        form.TheaterId = resolved.TheaterId;

        var today = _tz.ToLocalDate(_clock.GetUtcNow().UtcDateTime);
        if (form.Date is { } d && d < today)
            ModelState.AddModelError(nameof(form.Date), "Không xếp lịch cho ngày đã qua.");

        if (!ModelState.IsValid) return View(await BuildViewAsync(form, preview: false, ct));

        if (command != "apply") return View(await BuildViewAsync(form, preview: true, ct));

        // Áp dụng: dựng lại lịch ở server từ chính các tham số (không tin bảng xem trước phía client),
        // rồi kiểm trùng từng suất lần nữa trước khi ghi — lịch có thể đã đổi giữa lúc xem và lúc bấm.
        var (_, _, plan) = await BuildPlanAsync(form, ct);
        var priceOf = await PriceResolverAsync(form, ct);
        int created = 0, skipped = 0;
        var offset = _tz.Offset;

        foreach (var p in plan)
        {
            var showtime = new Showtime
            {
                MovieId = p.MovieId,
                RoomId = p.RoomId,
                StartTimeUtc = DateTime.SpecifyKind(p.StartLocal - offset, DateTimeKind.Utc),
                BasePrice = priceOf(p),
                Language = form.Language,
            };
            await _scheduler.PrepareAsync(showtime, ct);

            if (await FindOverlapAsync(p.RoomId, showtime.StartTimeUtc, showtime.EndTimeUtc, null, ct) is not null)
            {
                skipped++;
                continue;
            }

            _db.Showtimes.Add(showtime);
            await _db.SaveChangesAsync(ct);
            created++;
        }

        TempData["Message"] = $"Đã tạo {created} suất chiếu cho ngày {form.Date:dd/MM/yyyy}" +
                              (skipped > 0 ? $"; bỏ qua {skipped} suất vì trùng lịch." : ".");
        return RedirectToAction(nameof(Index), new { date = form.Date!.Value.ToString("yyyy-MM-dd"), theaterId = form.TheaterId });
    }

    /// <summary>Chi nhánh cần xếp lịch: BranchAdmin → chi nhánh mình; Admin → chọn và phải có quyền. Result khác null = trả luôn.</summary>
    private async Task<(int? TheaterId, IActionResult? Result)> ResolveTheaterAsync(int? theaterId, CancellationToken ct)
    {
        var scoped = await _branches.TheaterIdAsync(ct);
        if (scoped is not null)
        {
            if (theaterId is not null && theaterId != scoped) return (null, Forbid());
            theaterId = scoped;
        }
        else
        {
            theaterId ??= await _db.Theaters.AsNoTracking().Where(t => t.IsActive)
                .OrderBy(t => t.City).ThenBy(t => t.Name).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
            if (theaterId is null) return (null, RedirectToAction("Index", "Theaters"));
            if (!await _branches.CanAccessTheaterAsync(theaterId.Value, ct)) return (null, Forbid());
        }

        var active = await _db.Theaters.AsNoTracking().AnyAsync(t => t.Id == theaterId && t.IsActive, ct);
        return active ? (theaterId, null) : (null, NotFound());
    }

    /// <summary>Giá gợi ý ban đầu: giá thấp nhất trong các suất tương lai của chi nhánh (làm tròn 1.000đ); chưa có suất nào thì giữ mặc định.</summary>
    private async Task<decimal> SuggestBasePriceAsync(int theaterId, decimal fallback, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var prices = await _db.Showtimes.AsNoTracking()
            .Where(s => s.Room!.TheaterId == theaterId && s.StartTimeUtc > nowUtc)
            .Select(s => s.BasePrice).ToListAsync(ct);
        return prices.Count == 0 ? fallback : Math.Round(prices.Min() / 1000m) * 1000m;
    }

    private async Task<(List<PlanRoom> Rooms, List<PlanMovie> Movies, IReadOnlyList<PlannedShowtime> Plan)> BuildPlanAsync(
        AutoPlanForm form, CancellationToken ct)
    {
        var day = form.Date!.Value;
        var rooms = await _db.Rooms.AsNoTracking()
            .Where(r => r.TheaterId == form.TheaterId)
            .Select(r => new PlanRoom(r.Id, r.RoomName, r.Seats.Count))
            .ToListAsync(ct);

        var movies = await _db.Movies.AsNoTracking()
            .Where(m => m.IsActive && form.MovieIds.Contains(m.Id) && m.ReleaseDate <= day)
            .Select(m => new PlanMovie(m.Id, m.Title, m.DurationMinutes, m.ReleaseDate))
            .ToListAsync(ct);

        // Suất có sẵn chạm vào ngày này (kể cả suất qua đêm từ hôm trước) — để không xếp đè.
        var (fromUtc, toUtc) = _tz.DayRangeUtc(day);
        var roomIds = rooms.Select(r => r.RoomId).ToList();
        var busy = await _db.Showtimes.AsNoTracking()
            .Where(s => roomIds.Contains(s.RoomId) && s.EndTimeUtc > fromUtc - CleaningBuffer && s.StartTimeUtc < toUtc + CleaningBuffer)
            .Select(s => new { s.RoomId, s.StartTimeUtc, s.EndTimeUtc })
            .ToListAsync(ct);
        var existing = busy
            .GroupBy(b => b.RoomId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<(DateTime StartLocal, DateTime EndLocal)>)g
                    .Select(b => (_tz.ToLocalTime(b.StartTimeUtc), _tz.ToLocalTime(b.EndTimeUtc))).ToList());

        var nowLocal = _tz.ToLocalTime(_clock.GetUtcNow().UtcDateTime);
        var plan = ShowtimeAutoPlanner.Plan(
            day, rooms, movies, existing, CleaningBuffer,
            notBeforeLocal: day == DateOnly.FromDateTime(nowLocal) ? nowLocal : null);

        return (rooms, movies, plan);
    }

    private async Task<AutoPlanViewModel> BuildViewAsync(AutoPlanForm form, bool preview, CancellationToken ct)
    {
        var day = form.Date ?? _tz.ToLocalDate(_clock.GetUtcNow().UtcDateTime);
        var theater = await _db.Theaters.AsNoTracking().FirstAsync(t => t.Id == form.TheaterId, ct);

        var scoped = await _branches.TheaterIdAsync(ct);
        var theaters = await _db.Theaters.AsNoTracking()
            .Where(t => t.IsActive && (scoped == null || t.Id == scoped))
            .OrderBy(t => t.City).ThenBy(t => t.Name)
            .Select(t => new AutoPlanTheaterOption(t.Id, t.Name + " · " + t.City))
            .ToListAsync(ct);

        var all = await _db.Movies.AsNoTracking()
            .Where(m => m.IsActive && m.ReleaseDate <= day)
            .OrderByDescending(m => m.ReleaseDate).ThenBy(m => m.Title)
            .ToListAsync(ct);

        // Lần đầu mở trang: chọn sẵn tất cả phim đang chiếu.
        if (form.MovieIds.Count == 0 && !preview) form.MovieIds = all.Select(m => m.Id).ToList();
        var chosen = form.MovieIds.ToHashSet();
        var movies = all
            .Select(m => new AutoPlanMovieOption(m.Id, m.Title, m.DurationMinutes, m.ReleaseDate, chosen.Contains(m.Id)))
            .ToList();

        IReadOnlyList<PlannedShowtime>? proposed = null;
        IReadOnlyList<decimal>? prices = null;
        var summaries = new List<AutoPlanRoomSummary>();
        if (preview)
        {
            var (rooms, _, plan) = await BuildPlanAsync(form, ct);
            proposed = plan;
            var resolve = await PriceResolverAsync(form, ct);
            prices = plan.Select(resolve).ToList();
            summaries = rooms.OrderByDescending(r => r.Capacity)
                .Select(r => new AutoPlanRoomSummary(r.Name, r.Capacity, plan.Count(p => p.RoomId == r.RoomId)))
                .ToList();
        }

        return new AutoPlanViewModel(form, theater.Name, theaters, movies, proposed, prices, summaries, form.PrimeSurcharge);
    }

    /// <summary>
    /// Giá cơ bản của từng suất đề xuất: theo bảng giá chi nhánh (nếu bật và chi nhánh có bảng giá) — cùng công thức với
    /// trang bảng giá của khách — hoặc giá nhập tay + phụ thu giờ vàng.
    /// </summary>
    private async Task<Func<PlannedShowtime, decimal>> PriceResolverAsync(AutoPlanForm form, CancellationToken ct)
    {
        var pricing = form.UsePricing && form.TheaterId is { } id ? await PricingLoader.LoadAsync(_db, id, ct) : null;
        if (pricing is null)
            return p => form.BasePrice + (p.Prime ? form.PrimeSurcharge : 0m);

        var roomTypes = await _db.Rooms.AsNoTracking().Where(r => r.TheaterId == form.TheaterId)
            .ToDictionaryAsync(r => r.Id, r => r.RoomType, ct);
        return p => PricingRules.BasePrice(pricing, roomTypes.GetValueOrDefault(p.RoomId), p.StartLocal);
    }

    /// <summary>Giá gợi ý theo bảng giá chi nhánh cho (phòng, giờ bắt đầu) — form thêm suất gọi bằng fetch để điền sẵn giá.</summary>
    [HttpGet]
    public async Task<IActionResult> SuggestPrice(int roomId, DateTime start, CancellationToken ct)
    {
        var room = await _db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(room.TheaterId, ct)) return Forbid();

        var pricing = await PricingLoader.LoadAsync(_db, room.TheaterId, ct);
        if (pricing is null) return Json(new { found = false });

        var price = PricingRules.BasePrice(pricing, room.RoomType, start);
        return Json(new { found = true, price, thousands = price / 1000m, text = price.Vnd(), room = room.RoomType.ToString() });
    }

    /// <summary>Chỉ xóa được suất chưa có đơn nào — đơn đã đặt phải giữ lại để tra cứu và thống kê.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var showtime = await _db.Showtimes.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (showtime is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(await TheaterIdOfRoomAsync(showtime.RoomId, ct), ct))
            return Forbid();

        if (await _db.Bookings.AnyAsync(b => b.ShowtimeId == id, ct))
        {
            TempData["Error"] = "Không xóa được suất chiếu đã có vé được đặt.";
            return RedirectToIndexOf(showtime);
        }

        _db.Showtimes.Remove(showtime);
        await _db.SaveChangesAsync(ct);

        return RedirectToIndexOf(showtime);
    }

    /// <summary>
    /// Kiểm tra form rồi chép sang entity. MovieId và RoomId đến từ form nên có thể bị sửa tay:
    /// phải có thật và còn hoạt động (General.md §7.7). Trả false nếu ModelState có lỗi.
    /// </summary>
    private async Task<bool> TryApplyAsync(ShowtimeFormModel form, Showtime showtime, CancellationToken ct)
    {
        if (!ModelState.IsValid) return false;

        var movieOk = await _db.Movies.AnyAsync(m => m.Id == form.MovieId && m.IsActive, ct);
        if (!movieOk)
            ModelState.AddModelError(nameof(form.MovieId), "Phim không tồn tại hoặc đã ẩn.");

        var roomTheaterId = await _db.Rooms
            .Where(r => r.Id == form.RoomId && r.Theater!.IsActive)
            .Select(r => (int?)r.TheaterId)
            .FirstOrDefaultAsync(ct);

        if (roomTheaterId is null)
            ModelState.AddModelError(nameof(form.RoomId), "Phòng không tồn tại hoặc chi nhánh đã đóng.");
        else if (!await _branches.CanAccessTheaterAsync(roomTheaterId.Value, ct))
            ModelState.AddModelError(nameof(form.RoomId), "Bạn chỉ xếp được suất chiếu cho chi nhánh mình quản lý.");

        if (!ModelState.IsValid) return false;

        var candidate = new Showtime
        {
            MovieId = form.MovieId,
            StartTimeUtc = DateTime.SpecifyKind(form.StartTimeLocal!.Value - _tz.Offset, DateTimeKind.Utc),
        };
        await _scheduler.PrepareAsync(candidate, ct);

        // Chỉ chặn khi tạo mới hoặc đổi giờ bắt đầu: sửa giá/phim của suất đã chiếu không bị cản.
        var startChanged = showtime.Id == 0 || showtime.StartTimeUtc != candidate.StartTimeUtc;
        if (startChanged && candidate.StartTimeUtc < _clock.GetUtcNow().UtcDateTime)
        {
            ModelState.AddModelError(nameof(form.StartTimeLocal), "Giờ bắt đầu phải ở tương lai.");
            return false;
        }

        var clash = await FindOverlapAsync(form.RoomId, candidate.StartTimeUtc, candidate.EndTimeUtc, form.Id, ct);
        if (clash is not null)
        {
            ModelState.AddModelError(nameof(form.StartTimeLocal),
                $"Trùng lịch với suất \"{clash.Movie!.Title}\" lúc {_tz.ToLocalTime(clash.StartTimeUtc):HH:mm dd/MM} " +
                $"trong cùng phòng (cần cách nhau ít nhất {CleaningBuffer.TotalMinutes:0} phút để dọn phòng).");
            return false;
        }

        showtime.MovieId = form.MovieId;
        showtime.RoomId = form.RoomId;
        showtime.StartTimeUtc = candidate.StartTimeUtc;
        showtime.EndTimeUtc = candidate.EndTimeUtc;
        // Giá do bảng giá chi nhánh quyết định (loại phòng, giờ, ngày), admin không nhập tay. Suất đã có vé giữ nguyên giá.
        var hasBookings = showtime.Id != 0 && await _db.Bookings.AnyAsync(b => b.ShowtimeId == showtime.Id, ct);
        if (!hasBookings)
        {
            var pricing = await PricingLoader.LoadAsync(_db, roomTheaterId!.Value, ct);
            var roomType = await _db.Rooms.Where(r => r.Id == form.RoomId).Select(r => r.RoomType).FirstAsync(ct);
            showtime.BasePrice = pricing is null
                ? form.BasePrice
                : PricingRules.BasePrice(pricing, roomType, form.StartTimeLocal!.Value);
        }
        showtime.Language = form.Language;
        return true;
    }

    /// <summary>
    /// Hai suất cùng phòng trùng nhau khi khoảng [bắt đầu, kết thúc + dọn phòng) của chúng giao nhau.
    /// Chạm mép (suất sau bắt đầu đúng lúc suất trước dọn xong) thì hợp lệ.
    /// </summary>
    private Task<Showtime?> FindOverlapAsync(int roomId, DateTime startUtc, DateTime endUtc, int? excludeId, CancellationToken ct)
    {
        var endWithBuffer = endUtc + CleaningBuffer;
        var earliestConflictingEnd = startUtc - CleaningBuffer;

        return _db.Showtimes.AsNoTracking()
            .Include(s => s.Movie)
            .Where(s => s.RoomId == roomId && s.Id != excludeId)
            .Where(s => s.StartTimeUtc < endWithBuffer && s.EndTimeUtc > earliestConflictingEnd)
            .OrderBy(s => s.StartTimeUtc)
            .FirstOrDefaultAsync(ct);
    }

    private async Task FillOptionsAsync(ShowtimeFormModel form, CancellationToken ct)
    {
        form.Movies = await _db.Movies.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Title)
            .Select(m => new SelectListItem(m.Title + " (" + m.DurationMinutes + "')", m.Id.ToString()))
            .ToListAsync(ct);

        // BranchAdmin chỉ thấy phòng của chi nhánh mình trong dropdown.
        var scoped = await _branches.TheaterIdAsync(ct);
        var roomsQuery = _db.Rooms.AsNoTracking().Where(r => r.Theater!.IsActive);
        if (scoped is not null) roomsQuery = roomsQuery.Where(r => r.TheaterId == scoped);

        var rooms = await roomsQuery
            .OrderBy(r => r.Theater!.City).ThenBy(r => r.Theater!.Name).ThenBy(r => r.RoomName)
            .Select(r => new { r.Id, r.RoomName, r.Theater!.Name, r.Theater.City })
            .ToListAsync(ct);

        var groups = new Dictionary<string, SelectListGroup>();
        form.Rooms = rooms.Select(r =>
        {
            var key = r.City + " · " + r.Name;
            if (!groups.TryGetValue(key, out var group)) groups[key] = group = new SelectListGroup { Name = key };
            return new SelectListItem(r.RoomName, r.Id.ToString()) { Group = group };
        }).ToList();
    }

    private Task<int> TheaterIdOfRoomAsync(int roomId, CancellationToken ct) =>
        _db.Rooms.Where(r => r.Id == roomId).Select(r => r.TheaterId).FirstAsync(ct);

    private RedirectToActionResult RedirectToIndexOf(Showtime showtime) =>
        RedirectToAction(nameof(Index), new { date = _tz.ToLocalDate(showtime.StartTimeUtc).ToString("yyyy-MM-dd") });
}
