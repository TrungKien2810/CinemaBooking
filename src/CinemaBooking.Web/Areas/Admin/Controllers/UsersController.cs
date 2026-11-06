using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>
/// Quản lý tài khoản (chỉ Admin toàn hệ thống): xem/tìm tài khoản, tạo tài khoản quản lý chi nhánh (BranchAdmin) và gán
/// chi nhánh, khóa/mở khóa, đặt lại mật khẩu. Không cho tự khóa chính mình.
/// </summary>
[Authorize(Roles = AppRoles.Admin)]
public class UsersController : AdminControllerBase
{
    public const int PageSize = 30;

    private readonly CinemaDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public UsersController(CinemaDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    /// <param name="q">Tìm theo email, họ tên hoặc số điện thoại.</param>
    /// <param name="role">Admin, BranchAdmin hoặc Customer; bỏ trống = tất cả.</param>
    public async Task<IActionResult> Index(string? q, string? role, int? page, CancellationToken ct)
    {
        var selfId = _users.GetUserId(User);
        var roleFilter = AppRoles.All.FirstOrDefault(r => r == role);

        var query = from u in _db.Users.AsNoTracking()
                    select new
                    {
                        u.Id, u.Email, u.FullName, u.PhoneNumber, u.TheaterId, u.LockoutEnd,
                        RoleName = (from ur in _db.UserRoles
                                    join r in _db.Roles on ur.RoleId equals r.Id
                                    where ur.UserId == u.Id
                                    select r.Name).FirstOrDefault(),
                    };

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(u => (u.Email != null && u.Email.Contains(term))
                                     || (u.FullName != null && u.FullName.Contains(term))
                                     || (u.PhoneNumber != null && u.PhoneNumber.Contains(term)));
        }
        if (roleFilter is not null) query = query.Where(u => u.RoleName == roleFilter);

        var total = await query.CountAsync(ct);
        var current = PageInfo.Clamp(page, total, PageSize);
        var rows = await query.OrderBy(u => u.RoleName == AppRoles.Admin ? 0 : u.RoleName == AppRoles.BranchAdmin ? 1 : 2)
            .ThenBy(u => u.Email)
            .Skip((current - 1) * PageSize).Take(PageSize)
            .ToListAsync(ct);

        var theaters = await _db.Theaters.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var now = DateTimeOffset.UtcNow;
        var items = rows.Select(u => new UserListItem(
                u.Id, u.Email ?? "", u.FullName, u.PhoneNumber, u.RoleName ?? "—",
                u.TheaterId is { } id && theaters.TryGetValue(id, out var name) ? name : null,
                u.LockoutEnd is { } end && end > now, u.Id == selfId))
            .ToList();

        var customers = await _db.UserRoles.CountAsync(ur => _db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Customer), ct);
        var staff = await _db.UserRoles.CountAsync(ur => _db.Roles.Any(r => r.Id == ur.RoleId && r.Name != AppRoles.Customer), ct);

        return View(new UsersIndexViewModel(items, q?.Trim(), roleFilter, new PageInfo(current, PageSize, total), customers, staff));
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? theaterId, CancellationToken ct)
    {
        var form = new UserFormModel { TheaterId = theaterId };
        await FillTheatersAsync(form, ct);
        return View("Form", form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserFormModel form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(form.Password))
            ModelState.AddModelError(nameof(form.Password), "Nhập mật khẩu cho tài khoản mới.");
        await ValidateTheaterAsync(form, ct);
        if (!ModelState.IsValid) { await FillTheatersAsync(form, ct); return View("Form", form); }

        var email = form.Email.Trim();
        if (await _users.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(nameof(form.Email), "Email này đã có tài khoản.");
            await FillTheatersAsync(form, ct);
            return View("Form", form);
        }

        var user = new ApplicationUser
        {
            UserName = email, Email = email, EmailConfirmed = true, FullName = form.FullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(form.PhoneNumber) ? null : form.PhoneNumber.Trim(),
            TheaterId = form.TheaterId,
        };
        var result = await _users.CreateAsync(user, form.Password!);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(nameof(form.Password), e.Description);
            await FillTheatersAsync(form, ct);
            return View("Form", form);
        }
        await _users.AddToRoleAsync(user, AppRoles.BranchAdmin);

        TempData["Message"] = $"Đã tạo tài khoản quản lý chi nhánh {email}.";
        return RedirectToAction(nameof(Index), new { role = AppRoles.BranchAdmin });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();

        var form = new UserFormModel
        {
            Id = user.Id, Email = user.Email ?? "", FullName = user.FullName ?? "", PhoneNumber = user.PhoneNumber,
            TheaterId = user.TheaterId, Role = (await _users.GetRolesAsync(user)).FirstOrDefault() ?? "—",
        };
        await FillTheatersAsync(form, ct);
        return View("Form", form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, UserFormModel form, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();

        form.Id = id;
        form.Email = user.Email ?? "";   // email đăng nhập không đổi ở đây
        form.Role = (await _users.GetRolesAsync(user)).FirstOrDefault() ?? "—";
        var isBranchAdmin = form.Role == AppRoles.BranchAdmin;

        // Chỉ BranchAdmin mới có chi nhánh; Admin/khách không bắt buộc.
        if (!isBranchAdmin) ModelState.Remove(nameof(form.TheaterId));
        else await ValidateTheaterAsync(form, ct);
        if (!ModelState.IsValid) { await FillTheatersAsync(form, ct); return View("Form", form); }

        user.FullName = form.FullName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(form.PhoneNumber) ? null : form.PhoneNumber.Trim();
        if (isBranchAdmin) user.TheaterId = form.TheaterId;
        var update = await _users.UpdateAsync(user);
        if (!update.Succeeded)
        {
            foreach (var e in update.Errors) ModelState.AddModelError(string.Empty, e.Description);
            await FillTheatersAsync(form, ct);
            return View("Form", form);
        }

        // Đổi mật khẩu (tùy chọn): admin đặt mật khẩu mới trực tiếp.
        if (!string.IsNullOrWhiteSpace(form.Password))
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var reset = await _users.ResetPasswordAsync(user, token, form.Password);
            if (!reset.Succeeded)
            {
                foreach (var e in reset.Errors) ModelState.AddModelError(nameof(form.Password), e.Description);
                await FillTheatersAsync(form, ct);
                return View("Form", form);
            }
        }

        TempData["Message"] = $"Đã cập nhật tài khoản {user.Email}.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Khóa / mở khóa đăng nhập. Không tự khóa chính mình (sẽ mất quyền quản trị).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLock(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();

        if (user.Id == _users.GetUserId(User))
        {
            TempData["Error"] = "Bạn không thể khóa chính tài khoản đang đăng nhập.";
            return RedirectToAction(nameof(Index));
        }

        var locked = user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow;
        await _users.SetLockoutEnabledAsync(user, true);
        await _users.SetLockoutEndDateAsync(user, locked ? null : DateTimeOffset.UtcNow.AddYears(100));
        // Đổi security stamp để phiên đang đăng nhập bị vô hiệu ở lần kiểm tra kế tiếp.
        if (!locked) await _users.UpdateSecurityStampAsync(user);

        TempData["Message"] = locked ? $"Đã mở khóa {user.Email}." : $"Đã khóa {user.Email}.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateTheaterAsync(UserFormModel form, CancellationToken ct)
    {
        if (form.TheaterId is { } id && !await _db.Theaters.AnyAsync(t => t.Id == id && t.IsActive, ct))
            ModelState.AddModelError(nameof(form.TheaterId), "Chi nhánh không tồn tại hoặc đã đóng.");
    }

    private async Task FillTheatersAsync(UserFormModel form, CancellationToken ct) =>
        form.Theaters = await _db.Theaters.AsNoTracking().Where(t => t.IsActive)
            .OrderBy(t => t.City).ThenBy(t => t.Name)
            .Select(t => new SelectListItem(t.City + " · " + t.Name, t.Id.ToString()))
            .ToListAsync(ct);
}
