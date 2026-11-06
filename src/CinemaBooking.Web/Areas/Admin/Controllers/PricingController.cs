using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>
/// Bảng giá vé của chi nhánh: giá theo loại phòng, phụ thu loại ghế / giờ cao điểm / cuối tuần. Giá vé KHÔNG theo phim.
/// BranchAdmin chỉ sửa chi nhánh mình (kiểm bằng <see cref="IBranchScope"/> ở mọi action).
/// </summary>
public class PricingController : AdminControllerBase
{
    private readonly CinemaDbContext _db;
    private readonly IBranchScope _branches;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;

    public PricingController(CinemaDbContext db, IBranchScope branches, IOptions<DisplayTimeZone> tz, TimeProvider clock)
    {
        _db = db;
        _branches = branches;
        _tz = tz.Value;
        _clock = clock;
    }

    public async Task<IActionResult> Index(int? theaterId, CancellationToken ct)
    {
        var resolved = await ResolveAsync(theaterId, ct);
        if (resolved.Result is not null) return resolved.Result;

        var theater = await _db.Theaters.AsNoTracking().FirstAsync(t => t.Id == resolved.TheaterId, ct);
        var pricing = await _db.TheaterPricings.AsNoTracking().FirstOrDefaultAsync(p => p.TheaterId == theater.Id, ct)
                      ?? PricingRules.DefaultFor(theater.Id, 85_000m);

        var form = PricingFormModel.From(pricing, theater.Name);
        await FillAsync(form, ct);
        return View(form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(PricingFormModel form, CancellationToken ct)
    {
        var resolved = await ResolveAsync(form.TheaterId, ct);
        if (resolved.Result is not null) return resolved.Result;
        form.TheaterId = resolved.TheaterId!.Value;

        var theater = await _db.Theaters.AsNoTracking().FirstAsync(t => t.Id == form.TheaterId, ct);
        form.TheaterName = theater.Name;
        if (!ModelState.IsValid)
        {
            await FillAsync(form, ct);
            return View("Index", form);
        }

        var pricing = await _db.TheaterPricings.FirstOrDefaultAsync(p => p.TheaterId == form.TheaterId, ct);
        if (pricing is null)
        {
            pricing = new TheaterPricing { TheaterId = form.TheaterId };
            _db.TheaterPricings.Add(pricing);
        }
        form.ApplyTo(pricing);
        pricing.UpdatedUtc = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);

        TempData["Message"] = "Đã lưu bảng giá. Giá mới áp dụng cho suất tạo từ giờ; dùng nút bên dưới để áp cho các suất đã có.";
        return RedirectToAction(nameof(Index), new { theaterId = form.TheaterId });
    }

    /// <summary>
    /// Tính lại giá cơ bản của các suất CHƯA CHIẾU theo bảng giá hiện tại. Suất đã có vé giữ nguyên (khách đã mua theo giá cũ
    /// và sơ đồ ghế đang hiển thị giá đó); suất đã chiếu không đụng tới.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyToShowtimes(int theaterId, CancellationToken ct)
    {
        var resolved = await ResolveAsync(theaterId, ct);
        if (resolved.Result is not null) return resolved.Result;

        var pricing = await _db.TheaterPricings.AsNoTracking().FirstOrDefaultAsync(p => p.TheaterId == theaterId, ct);
        if (pricing is null)
        {
            TempData["Error"] = "Chi nhánh chưa có bảng giá — hãy lưu bảng giá trước.";
            return RedirectToAction(nameof(Index), new { theaterId });
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var showtimes = await _db.Showtimes
            .Include(s => s.Room)
            .Where(s => s.Room!.TheaterId == theaterId && s.StartTimeUtc > nowUtc)
            .ToListAsync(ct);
        var booked = (await _db.Bookings.AsNoTracking()
                .Where(b => b.Showtime!.Room!.TheaterId == theaterId && b.Showtime.StartTimeUtc > nowUtc)
                .Select(b => b.ShowtimeId).Distinct().ToListAsync(ct))
            .ToHashSet();

        int updated = 0, skipped = 0;
        foreach (var s in showtimes)
        {
            if (booked.Contains(s.Id)) { skipped++; continue; }

            var price = PricingRules.BasePrice(pricing, s.Room!.RoomType, _tz.ToLocalTime(s.StartTimeUtc));
            if (s.BasePrice == price) continue;
            s.BasePrice = price;
            updated++;
        }
        await _db.SaveChangesAsync(ct);

        TempData["Message"] = $"Đã cập nhật giá cho {updated} suất chưa chiếu" +
                              (skipped > 0 ? $"; giữ nguyên {skipped} suất đã có vé." : ".");
        return RedirectToAction(nameof(Index), new { theaterId });
    }

    private async Task<(int? TheaterId, IActionResult? Result)> ResolveAsync(int? theaterId, CancellationToken ct)
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

        var exists = await _db.Theaters.AsNoTracking().AnyAsync(t => t.Id == theaterId, ct);
        return exists ? (theaterId, null) : (null, NotFound());
    }

    private async Task FillAsync(PricingFormModel form, CancellationToken ct)
    {
        var scoped = await _branches.TheaterIdAsync(ct);
        form.Theaters = await _db.Theaters.AsNoTracking()
            .Where(t => t.IsActive && (scoped == null || t.Id == scoped))
            .OrderBy(t => t.City).ThenBy(t => t.Name)
            .Select(t => new SelectListItem(t.City + " · " + t.Name, t.Id.ToString(), t.Id == form.TheaterId))
            .ToListAsync(ct);

        // Xem trước bằng ĐÚNG giá đang nhập (chưa lưu), cho mọi loại phòng.
        form.Preview = PriceMatrixModel.Build(form.ToPricing(), Enum.GetValues<RoomType>());
    }
}
