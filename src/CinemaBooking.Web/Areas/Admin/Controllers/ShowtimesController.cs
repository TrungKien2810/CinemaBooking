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

    public async Task<IActionResult> Index(int? theaterId, DateOnly? date, CancellationToken ct)
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
                Bookings = _db.Bookings.Count(b => b.ShowtimeId == s.Id),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new ShowtimeAdminItem(
                r.Id, r.Title, r.TheaterName, r.RoomName,
                _tz.ToLocalTime(r.StartTimeUtc), _tz.ToLocalTime(r.EndTimeUtc), r.BasePrice, r.Bookings))
            .ToList();

        var theatersQuery = _db.Theaters.AsNoTracking().Where(t => t.IsActive);
        if (scoped is not null) theatersQuery = theatersQuery.Where(t => t.Id == scoped);

        var theaters = await theatersQuery
            .OrderBy(t => t.City).ThenBy(t => t.Name)
            .Select(t => new SelectListItem(t.City + " · " + t.Name, t.Id.ToString()))
            .ToListAsync(ct);

        return View(new ShowtimesIndexViewModel(day, theaterId, theaters, items));
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
        showtime.BasePrice = form.BasePrice;
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
