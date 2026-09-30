using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

public sealed class BranchDirectory : IBranchDirectory
{
    private readonly CinemaDbContext _db;

    public BranchDirectory(CinemaDbContext db) => _db = db;

    public async Task<IReadOnlyList<BranchListItem>> GetActiveAsync(CancellationToken ct = default) =>
        await _db.Theaters.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.City)
            .ThenBy(t => t.Name)
            .Select(t => new BranchListItem(t.Id, t.Name, t.City, t.Address, t.Rooms.Count))
            .ToListAsync(ct);
}
