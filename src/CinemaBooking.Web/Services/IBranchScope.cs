using System.Security.Claims;
using CinemaBooking.Domain;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Phạm vi chi nhánh của người đang đăng nhập.
/// <para>
/// Admin toàn hệ thống → không giới hạn. BranchAdmin → chỉ chi nhánh gắn trong
/// <c>ApplicationUser.TheaterId</c>. Phạm vi đọc từ DB, KHÔNG đọc tham số trên URL: nếu tin
/// <c>?theaterId=</c> thì branch admin chỉ cần sửa URL là quản lý được chi nhánh khác.
/// </para>
/// </summary>
public interface IBranchScope
{
    /// <summary>
    /// Id chi nhánh bị giới hạn; null = Admin toàn hệ thống (không giới hạn). BranchAdmin CHƯA được gán chi nhánh
    /// (hoặc chi nhánh đã bị gỡ) nhận <see cref="BranchScope.NoBranch"/> — một id không tồn tại — để mọi danh sách
    /// rỗng và mọi kiểm tra quyền đều từ chối, thay vì bị nhầm thành "không giới hạn".
    /// </summary>
    Task<int?> TheaterIdAsync(CancellationToken ct = default);

    /// <summary>Người dùng hiện tại có được thao tác trên chi nhánh này không.</summary>
    Task<bool> CanAccessTheaterAsync(int theaterId, CancellationToken ct = default);
}

public sealed class BranchScope : IBranchScope
{
    /// <summary>Id giả cho BranchAdmin chưa có chi nhánh: không trùng chi nhánh thật nào (Identity id bắt đầu từ 1).</summary>
    public const int NoBranch = -1;

    private readonly IHttpContextAccessor _http;
    private readonly CinemaDbContext _db;

    // Một request có thể hỏi nhiều lần (Index + từng action con) — cache trong scope.
    private bool _resolved;
    private int? _theaterId;

    public BranchScope(IHttpContextAccessor http, CinemaDbContext db)
    {
        _http = http;
        _db = db;
    }

    public async Task<int?> TheaterIdAsync(CancellationToken ct = default)
    {
        await ResolveAsync(ct);
        return _theaterId;
    }

    public async Task<bool> CanAccessTheaterAsync(int theaterId, CancellationToken ct = default)
    {
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return false;

        // Admin toàn hệ thống: không giới hạn.
        if (user.IsInRole(AppRoles.Admin)) return true;

        await ResolveAsync(ct);
        return _theaterId == theaterId;
    }

    private async Task ResolveAsync(CancellationToken ct)
    {
        if (_resolved) return;
        _resolved = true;

        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return;
        if (user.IsInRole(AppRoles.Admin)) return;      // null = toàn hệ thống
        if (!user.IsInRole(AppRoles.BranchAdmin)) return;

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return;

        var assigned = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TheaterId)
            .FirstOrDefaultAsync(ct);

        // Fail-closed: BranchAdmin không có chi nhánh → NoBranch (không phải null = toàn hệ thống).
        _theaterId = assigned ?? NoBranch;
    }
}
