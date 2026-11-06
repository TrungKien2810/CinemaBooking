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
/// Mã giảm giá khách nhập khi thanh toán. Admin tạo mã cho mọi chi nhánh hoặc một chi nhánh; BranchAdmin chỉ tạo/sửa mã
/// của CHI NHÁNH MÌNH (không đụng được mã toàn hệ thống). Mã đã có người dùng thì không xóa hẳn, chỉ tạm ngưng.
/// </summary>
public class VouchersController : AdminControllerBase
{
    public const int PageSize = 30;

    private readonly CinemaDbContext _db;
    private readonly IBranchScope _branches;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;

    public VouchersController(CinemaDbContext db, IBranchScope branches, IOptions<DisplayTimeZone> tz, TimeProvider clock)
    {
        _db = db;
        _branches = branches;
        _tz = tz.Value;
        _clock = clock;
    }

    /// <param name="q">Tìm theo mã hoặc tên chương trình.</param>
    /// <param name="status">"running" = đang chạy, "ended" = hết hạn, "off" = tạm ngưng.</param>
    public async Task<IActionResult> Index(string? q, string? status, int? page, CancellationToken ct)
    {
        var scoped = await _branches.TheaterIdAsync(ct);
        var today = _tz.ToLocalDate(_clock.GetUtcNow().UtcDateTime);

        var query = _db.Vouchers.AsNoTracking().AsQueryable();
        // BranchAdmin chỉ thấy mã của chi nhánh mình (kể cả khi chưa được gán chi nhánh: NoBranch → không có gì).
        if (scoped is not null) query = query.Where(v => v.TheaterId == scoped);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var upper = term.ToUpperInvariant();
            query = query.Where(v => v.Code.Contains(upper) || v.Title.Contains(term));
        }

        var statusFilter = status is "running" or "ended" or "off" ? status : null;
        query = statusFilter switch
        {
            "running" => query.Where(v => v.IsActive && v.StartDate <= today && v.EndDate >= today),
            "ended" => query.Where(v => v.EndDate < today),
            "off" => query.Where(v => !v.IsActive),
            _ => query,
        };

        var total = await query.CountAsync(ct);
        var current = PageInfo.Clamp(page, total, PageSize);
        var rows = await query.OrderByDescending(v => v.IsActive).ThenByDescending(v => v.EndDate).ThenBy(v => v.Code)
            .Skip((current - 1) * PageSize).Take(PageSize)
            .Select(v => new { Voucher = v, TheaterName = v.Theater != null ? v.Theater.Name : null })
            .ToListAsync(ct);

        var codes = rows.Select(r => r.Voucher.Code).ToList();
        var usage = await _db.Bookings.AsNoTracking()
            .Where(b => b.VoucherCode != null && codes.Contains(b.VoucherCode)
                        && (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed))
            .GroupBy(b => b.VoucherCode!)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Code, x => x.Count, ct);

        var items = rows.Select(r => new VoucherListItem(
                r.Voucher.Id, r.Voucher.Code, r.Voucher.Title, r.TheaterName ?? "Mọi chi nhánh",
                VoucherRules.Describe(r.Voucher), r.Voucher.StartDate, r.Voucher.EndDate,
                usage.GetValueOrDefault(r.Voucher.Code), r.Voucher.UsageLimit, r.Voucher.IsActive,
                r.Voucher.IsActive && r.Voucher.StartDate <= today && r.Voucher.EndDate >= today,
                CanEdit: scoped is null || r.Voucher.TheaterId == scoped))
            .ToList();

        return View(new VouchersIndexViewModel(items, q?.Trim(), statusFilter, new PageInfo(current, PageSize, total), total));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var today = _tz.ToLocalDate(_clock.GetUtcNow().UtcDateTime);
        var form = new VoucherFormModel { StartDate = today, EndDate = today.AddDays(30) };
        await PrepareAsync(form, ct);
        return View("Form", form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(VoucherFormModel form, CancellationToken ct)
    {
        await PrepareAsync(form, ct);
        if (!await ValidateAsync(form, null, ct)) return View("Form", form);

        var voucher = new Voucher();
        form.ApplyTo(voucher);
        _db.Vouchers.Add(voucher);
        await _db.SaveChangesAsync(ct);

        TempData["Message"] = $"Đã tạo mã {voucher.Code}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var voucher = await _db.Vouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
        if (voucher is null) return NotFound();
        if (!await CanManageAsync(voucher, ct)) return Forbid();

        var form = VoucherFormModel.From(voucher);
        await PrepareAsync(form, ct);
        return View("Form", form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, VoucherFormModel form, CancellationToken ct)
    {
        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (voucher is null) return NotFound();
        if (!await CanManageAsync(voucher, ct)) return Forbid();

        form.Id = id;
        await PrepareAsync(form, ct);
        if (!await ValidateAsync(form, id, ct)) return View("Form", form);

        // Đổi mã khi đã có người dùng làm lịch sử đơn mất liên kết: giữ nguyên mã cũ.
        var used = await _db.Bookings.AnyAsync(b => b.VoucherCode == voucher.Code, ct);
        if (used && !string.Equals(form.Code.Trim(), voucher.Code, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(form.Code), "Mã đã có người dùng nên không đổi được. Hãy tạo mã mới.");
            return View("Form", form);
        }

        form.ApplyTo(voucher);
        await _db.SaveChangesAsync(ct);

        TempData["Message"] = $"Đã cập nhật mã {voucher.Code}.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Đã có đơn dùng mã thì chỉ tạm ngưng (giữ lịch sử); chưa ai dùng thì xóa hẳn.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (voucher is null) return NotFound();
        if (!await CanManageAsync(voucher, ct)) return Forbid();

        if (await _db.Bookings.AnyAsync(b => b.VoucherCode == voucher.Code, ct))
        {
            voucher.IsActive = false;
            TempData["Message"] = $"Mã {voucher.Code} đã có người dùng nên chỉ được tạm ngưng.";
        }
        else
        {
            _db.Vouchers.Remove(voucher);
            TempData["Message"] = $"Đã xóa mã {voucher.Code}.";
        }
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>BranchAdmin chỉ quản lý mã của chi nhánh mình; mã toàn hệ thống (TheaterId null) chỉ Admin.</summary>
    private async Task<bool> CanManageAsync(Voucher voucher, CancellationToken ct)
    {
        var scoped = await _branches.TheaterIdAsync(ct);
        if (scoped is null) return true;
        return voucher.TheaterId is { } t && await _branches.CanAccessTheaterAsync(t, ct);
    }

    private async Task PrepareAsync(VoucherFormModel form, CancellationToken ct)
    {
        var scoped = await _branches.TheaterIdAsync(ct);
        var theaters = _db.Theaters.AsNoTracking().Where(t => t.IsActive);
        if (scoped is not null)
        {
            // Chi nhánh lấy từ phạm vi tài khoản, KHÔNG tin giá trị trong form.
            theaters = theaters.Where(t => t.Id == scoped);
            form.TheaterId = scoped;
            form.TheaterLocked = true;
        }

        form.Theaters = await theaters.OrderBy(t => t.City).ThenBy(t => t.Name)
            .Select(t => new SelectListItem(t.City + " · " + t.Name, t.Id.ToString()))
            .ToListAsync(ct);
    }

    private async Task<bool> ValidateAsync(VoucherFormModel form, int? excludeId, CancellationToken ct)
    {
        if (form.TheaterId is { } theaterId)
        {
            if (!await _branches.CanAccessTheaterAsync(theaterId, ct))
                ModelState.AddModelError(nameof(form.TheaterId), "Bạn không quản lý chi nhánh này.");
            else if (!await _db.Theaters.AnyAsync(t => t.Id == theaterId && t.IsActive, ct))
                ModelState.AddModelError(nameof(form.TheaterId), "Chi nhánh không tồn tại hoặc đã đóng.");
        }
        else if (await _branches.TheaterIdAsync(ct) is not null)
        {
            ModelState.AddModelError(nameof(form.TheaterId), "Chọn chi nhánh áp dụng.");
        }

        var code = (form.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length > 0 && await _db.Vouchers.AnyAsync(v => v.Code == code && v.Id != excludeId, ct))
            ModelState.AddModelError(nameof(form.Code), "Mã này đã tồn tại.");

        return ModelState.IsValid;
    }
}
