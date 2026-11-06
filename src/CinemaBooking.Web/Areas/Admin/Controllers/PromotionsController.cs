using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>
/// Khuyến mãi riêng từng chi nhánh, hiện ở trang chi tiết chi nhánh phía khách. Chủ chi nhánh chỉ
/// quản lý được khuyến mãi của chi nhánh mình — phạm vi lấy từ <see cref="IBranchScope"/>, không tin URL/form.
/// </summary>
public class PromotionsController : AdminControllerBase
{
    private readonly CinemaDbContext _db;
    private readonly IBranchScope _branches;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;

    public PromotionsController(CinemaDbContext db, IBranchScope branches, IOptions<DisplayTimeZone> tz, TimeProvider clock)
    {
        _db = db;
        _branches = branches;
        _tz = tz.Value;
        _clock = clock;
    }

    public async Task<IActionResult> Index(int? theaterId, CancellationToken ct)
    {
        var scoped = await _branches.TheaterIdAsync(ct);
        if (scoped is not null)
        {
            if (theaterId is not null && theaterId != scoped) return Forbid();
            theaterId = scoped;
        }
        else if (theaterId is null)
        {
            return RedirectToAction("Index", "Theaters");
        }

        var theater = await _db.Theaters.AsNoTracking().FirstOrDefaultAsync(t => t.Id == theaterId, ct);
        if (theater is null) return NotFound();

        var today = _tz.ToLocalDate(_clock.GetUtcNow().UtcDateTime);
        var items = (await _db.Promotions.AsNoTracking()
                .Where(p => p.TheaterId == theaterId)
                .OrderByDescending(p => p.EndDate)
                .ToListAsync(ct))
            .Select(p => new PromotionListItem(
                p.Id, p.Title, p.Badge, p.StartDate, p.EndDate, p.IsActive,
                p.IsActive && p.StartDate <= today && p.EndDate >= today,
                p.AppliesAutomatically ? PromotionRules.Describe(p) : "Chỉ quảng bá (không tự trừ giá)"))
            .ToList();

        return View(new PromotionsIndexViewModel(theater.Id, theater.Name, items));
    }

    [HttpGet]
    public async Task<IActionResult> Create(int theaterId, CancellationToken ct)
    {
        if (!await _branches.CanAccessTheaterAsync(theaterId, ct)) return Forbid();

        var theater = await _db.Theaters.AsNoTracking().FirstOrDefaultAsync(t => t.Id == theaterId, ct);
        if (theater is null) return NotFound();

        var today = _tz.ToLocalDate(_clock.GetUtcNow().UtcDateTime);
        return View("Form", new PromotionFormModel
        {
            TheaterId = theater.Id, TheaterName = theater.Name, StartDate = today, EndDate = today.AddDays(30),
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PromotionFormModel form, CancellationToken ct)
    {
        // TheaterId đến từ form: phải có thật và nằm trong phạm vi người dùng.
        if (!await _branches.CanAccessTheaterAsync(form.TheaterId, ct)) return Forbid();

        var theater = await _db.Theaters.AsNoTracking().FirstOrDefaultAsync(t => t.Id == form.TheaterId, ct);
        if (theater is null) return NotFound();

        form.TheaterName = theater.Name;
        if (!ModelState.IsValid) return View("Form", form);

        var promotion = new Promotion { TheaterId = theater.Id };
        Apply(form, promotion);
        _db.Promotions.Add(promotion);
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index), new { theaterId = theater.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var promotion = await _db.Promotions.AsNoTracking().Include(p => p.Theater).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (promotion is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(promotion.TheaterId, ct)) return Forbid();

        return View("Form", PromotionFormModel.From(promotion, promotion.Theater!.Name));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PromotionFormModel form, CancellationToken ct)
    {
        var promotion = await _db.Promotions.Include(p => p.Theater).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (promotion is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(promotion.TheaterId, ct)) return Forbid();

        // Chi nhánh của khuyến mãi lấy từ DB — sửa không được "dời" nó sang chi nhánh khác.
        form.Id = id;
        form.TheaterId = promotion.TheaterId;
        form.TheaterName = promotion.Theater!.Name;
        if (!ModelState.IsValid) return View("Form", form);

        Apply(form, promotion);
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index), new { theaterId = promotion.TheaterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var promotion = await _db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (promotion is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(promotion.TheaterId, ct)) return Forbid();

        _db.Promotions.Remove(promotion);
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index), new { theaterId = promotion.TheaterId });
    }

    private static void Apply(PromotionFormModel form, Promotion promotion)
    {
        promotion.Title = form.Title.Trim();
        promotion.Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
        promotion.Badge = string.IsNullOrWhiteSpace(form.Badge) ? null : form.Badge.Trim();
        promotion.StartDate = form.StartDate!.Value;
        promotion.EndDate = form.EndDate!.Value;
        promotion.IsActive = form.IsActive;

        // "none" xóa luôn mọi quy tắc cũ: điều kiện ngày/giờ/ghế vô nghĩa khi không có mức giảm.
        var percent = form.DiscountKind == "percent";
        var fixedPrice = form.DiscountKind == "fixed";
        promotion.DiscountPercent = percent ? form.DiscountPercent : null;
        promotion.FixedPrice = fixedPrice ? form.FixedPrice : null;
        var automatic = percent || fixedPrice;
        promotion.ApplicableSeatType = automatic ? form.ApplicableSeatType : null;
        promotion.DaysOfWeekMask = automatic ? form.DaysOfWeekMask : 0;
        promotion.FromTime = automatic ? form.FromTime : null;
        promotion.ToTime = automatic ? form.ToTime : null;
    }
}
