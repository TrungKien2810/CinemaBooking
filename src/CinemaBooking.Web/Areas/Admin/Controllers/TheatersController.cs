using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>Quản lý chi nhánh. Tên class giữ theo entity <see cref="Theater"/>.</summary>
public class TheatersController : AdminControllerBase
{
    private readonly CinemaDbContext _db;

    public TheatersController(CinemaDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var theaters = await _db.Theaters.AsNoTracking()
            .OrderBy(t => t.City)
            .ThenBy(t => t.Name)
            .Select(t => new TheaterListItem(t.Id, t.Name, t.City, t.Address, t.IsActive, t.Rooms.Count))
            .ToListAsync(ct);

        return View(theaters);
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new TheaterFormModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TheaterFormModel form, CancellationToken ct)
    {
        await ValidateUniqueNameAsync(form, ct);
        if (!ModelState.IsValid) return View("Form", form);

        var theater = new Theater();
        form.ApplyTo(theater);
        _db.Theaters.Add(theater);
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var theater = await _db.Theaters.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        return theater is null ? NotFound() : View("Form", TheaterFormModel.From(theater));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TheaterFormModel form, CancellationToken ct)
    {
        var theater = await _db.Theaters.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (theater is null) return NotFound();

        form.Id = id;
        await ValidateUniqueNameAsync(form, ct);
        if (!ModelState.IsValid) return View("Form", form);

        form.ApplyTo(theater);
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Soft delete: chi nhánh đóng cửa vẫn giữ phòng, suất và đơn cũ để tra cứu và thống kê.
    /// Khu khách lọc theo <see cref="Theater.IsActive"/>.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var theater = await _db.Theaters.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (theater is null) return NotFound();

        theater.IsActive = false;
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Tên chi nhánh có unique index. Kiểm tra trước để trả lỗi ngay trên ô nhập thay vì để
    /// SaveChanges nổ DbUpdateException thành trang lỗi 500.
    /// </summary>
    private async Task ValidateUniqueNameAsync(TheaterFormModel form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(form.Name)) return;

        var name = form.Name.Trim();
        var taken = await _db.Theaters.AnyAsync(t => t.Name == name && t.Id != form.Id, ct);
        if (taken)
            ModelState.AddModelError(nameof(form.Name), "Đã có chi nhánh trùng tên.");
    }
}
