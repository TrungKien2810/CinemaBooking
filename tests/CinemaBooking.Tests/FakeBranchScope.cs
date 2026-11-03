using CinemaBooking.Web.Services;

namespace CinemaBooking.Tests;

/// <summary>
/// Thay <see cref="IBranchScope"/> thật (đọc HttpContext + DB) bằng phạm vi cố định:
/// <c>isAdmin: true</c> = admin toàn hệ thống; ngược lại chỉ được chi nhánh <c>theaterId</c>.
/// </summary>
internal sealed class FakeBranchScope : IBranchScope
{
    private readonly bool _isAdmin;
    private readonly int? _theaterId;

    public FakeBranchScope(int? theaterId = null, bool isAdmin = true)
    {
        _isAdmin = isAdmin;
        _theaterId = theaterId;
    }

    public static FakeBranchScope Admin() => new(isAdmin: true);

    public static FakeBranchScope Branch(int theaterId) => new(theaterId, isAdmin: false);

    public Task<int?> TheaterIdAsync(CancellationToken ct = default) =>
        Task.FromResult(_isAdmin ? null : _theaterId);

    public Task<bool> CanAccessTheaterAsync(int theaterId, CancellationToken ct = default) =>
        Task.FromResult(_isAdmin || _theaterId == theaterId);
}
