using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

public static class PricingLoader
{
    public static Task<TheaterPricing?> LoadAsync(CinemaDbContext db, int theaterId, CancellationToken ct = default) =>
        db.TheaterPricings.AsNoTracking().FirstOrDefaultAsync(p => p.TheaterId == theaterId, ct);

    /// <summary>Phụ thu theo loại ghế của chi nhánh; null nếu chi nhánh chưa có bảng giá (dùng công thức cũ).</summary>
    public static async Task<SeatSurcharges?> LoadSurchargesAsync(CinemaDbContext db, int theaterId, CancellationToken ct = default)
    {
        var p = await LoadAsync(db, theaterId, ct);
        return p is null ? null : PricingRules.Surcharges(p);
    }
}
