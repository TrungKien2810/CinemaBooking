using CinemaBooking.Web.Services;
using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>Quản lý chi nhánh. Tên class giữ theo entity <see cref="Theater"/>.</summary>
[Authorize(Roles = AppRoles.Admin)]
public class TheatersController : AdminControllerBase
{
    private readonly CinemaDbContext _db;
    private readonly TimeProvider _clock;

    public TheatersController(CinemaDbContext db, TimeProvider? clock = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
    }

    /// <param name="q">Tìm theo tên, địa chỉ hoặc thành phố (không phân biệt dấu).</param>
    /// <param name="status">"active" = đang hoạt động, "closed" = đã đóng; bỏ trống = tất cả.</param>
    public async Task<IActionResult> Index(string? q, string? city, string? status, CancellationToken ct)
    {
        var all = await _db.Theaters.AsNoTracking()
            .OrderBy(t => t.City)
            .ThenBy(t => t.Name)
            .Select(t => new TheaterListItem(t.Id, t.Name, t.City, t.Address, t.IsActive, t.Rooms.Count, t.Phone,
                _db.Users.Where(u => u.TheaterId == t.Id).Select(u => u.Email).FirstOrDefault()))
            .ToListAsync(ct);

        var cities = all.Select(t => t.City).Distinct().ToList();
        var selectedCity = cities.FirstOrDefault(c => string.Equals(c, city?.Trim(), StringComparison.OrdinalIgnoreCase));
        var keyword = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        var statusFilter = status is "active" or "closed" ? status : null;

        var items = all
            .Where(t => selectedCity is null || t.City == selectedCity)
            .Where(t => statusFilter is null || t.IsActive == (statusFilter == "active"))
            .Where(t => TextSearch.Matches(keyword, t.Name, t.Address, t.City))
            .ToList();

        return View(new TheatersIndexViewModel(items, keyword, selectedCity, statusFilter, cities, all.Count));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await FillCitiesAsync(ct);
        return View("Form", new TheaterFormModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TheaterFormModel form, CancellationToken ct)
    {
        await ValidateUniqueNameAsync(form, ct);
        await UseExistingCitySpellingAsync(form, ct);
        if (!ModelState.IsValid)
        {
            await FillCitiesAsync(ct);
            return View("Form", form);
        }

        var theater = new Theater();
        form.ApplyTo(theater);
        _db.Theaters.Add(theater);
        await _db.SaveChangesAsync(ct);

        Notify($"Đã thêm chi nhánh {theater.Name}.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var theater = await _db.Theaters.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (theater is null) return NotFound();

        await FillCitiesAsync(ct);
        return View("Form", TheaterFormModel.From(theater));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TheaterFormModel form, CancellationToken ct)
    {
        var theater = await _db.Theaters.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (theater is null) return NotFound();

        form.Id = id;
        await ValidateUniqueNameAsync(form, ct);
        await UseExistingCitySpellingAsync(form, ct);

        // Bỏ tick hoạt động trong form cũng là đóng chi nhánh: phải qua đúng quy tắc như nút Đóng.
        if (theater.IsActive && !form.IsActive && await UpcomingTicketsAsync(id, ct) is > 0 and var sold)
            ModelState.AddModelError(nameof(form.IsActive), CloseBlockedMessage(sold));

        if (!ModelState.IsValid)
        {
            await FillCitiesAsync(ct);
            return View("Form", form);
        }

        form.ApplyTo(theater);
        await _db.SaveChangesAsync(ct);

        Notify($"Đã lưu chi nhánh {theater.Name}.");
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

        if (await UpcomingTicketsAsync(id, ct) is > 0 and var sold)
        {
            NotifyError(CloseBlockedMessage(sold));
            return RedirectToAction(nameof(Index));
        }

        theater.IsActive = false;
        await _db.SaveChangesAsync(ct);

        Notify($"Đã đóng chi nhánh {theater.Name}.");
        return RedirectToAction(nameof(Index));
    }

    private Task<int> UpcomingTicketsAsync(int theaterId, CancellationToken ct) =>
        SoldTickets.UpcomingForTheaterAsync(_db, theaterId, _clock.GetUtcNow().UtcDateTime, ct);

    private static string CloseBlockedMessage(int sold) =>
        $"Chi nhánh còn {sold} đơn của các suất chưa chiếu nên chưa đóng được. Đợi chiếu xong, hoặc hủy các suất đó ở mục Suất chiếu.";

    /// <summary>
    /// Thành phố gõ tay: "hà nội" hay "ha noi" phải về đúng "Hà Nội" đã có, nếu không trang khách sẽ
    /// hiện hai thành phố trùng nhau.
    /// </summary>
    private async Task UseExistingCitySpellingAsync(TheaterFormModel form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(form.City)) return;

        var typed = TextSearch.Normalize(form.City);
        var cities = await _db.Theaters.AsNoTracking().Select(t => t.City).Distinct().ToListAsync(ct);
        form.City = cities.FirstOrDefault(c => TextSearch.Normalize(c) == typed) ?? form.City.Trim();
    }

    private async Task FillCitiesAsync(CancellationToken ct) =>
        ViewData["Cities"] = await _db.Theaters.AsNoTracking().Select(t => t.City).Distinct().OrderBy(c => c).ToListAsync(ct);

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
