using CinemaBooking.Domain;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Controllers;

/// <summary>
/// Trang "Bảng giá vé" công khai: khách biết rạp tính giá thế nào (loại phòng, loại ghế, giờ chiếu, ngày) trước khi chọn phim.
/// Dựng từ ĐÚNG công thức tính giá thật (<see cref="PricingRules"/>) nên không thể lệch với giá ở bước chọn ghế.
/// </summary>
public class PricesController : Controller
{
    private readonly CinemaDbContext _db;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;

    public PricesController(CinemaDbContext db, IOptions<DisplayTimeZone> tz, TimeProvider clock)
    {
        _db = db;
        _tz = tz.Value;
        _clock = clock;
    }

    public async Task<IActionResult> Index(int? theaterId, CancellationToken ct)
    {
        var theaters = await _db.Theaters.AsNoTracking().Where(t => t.IsActive)
            .OrderBy(t => t.City).ThenBy(t => t.Name)
            .Select(t => new { t.Id, t.Name, t.City, t.Address, t.Phone, t.OpeningHours })
            .ToListAsync(ct);
        var options = theaters.Select(t => new PricesBranchOption(t.Id, t.Name, t.City)).ToList();

        // theaterId đến từ URL: chỉ nhận chi nhánh còn hoạt động, ngược lại lấy chi nhánh đầu danh sách.
        var chosen = theaters.FirstOrDefault(t => t.Id == theaterId) ?? theaters.FirstOrDefault();

        var pricings = await _db.TheaterPricings.AsNoTracking().ToDictionaryAsync(p => p.TheaterId, ct);
        var compare = new List<BranchPriceSummary>();
        foreach (var t in theaters)
        {
            if (!pricings.TryGetValue(t.Id, out var p)) continue;
            // Phòng 2D: thứ Hai trước giờ cao điểm, thứ Hai từ giờ cao điểm, thứ Bảy từ giờ cao điểm (độc lập với việc bảng chi tiết ẩn dòng nào).
            var monday = new DateTime(2026, 11, 2);
            var saturday = new DateTime(2026, 11, 7);
            compare.Add(new BranchPriceSummary(
                t.Id, t.Name, t.City,
                PricingRules.BasePrice(p, RoomType.Standard, monday.AddHours(Math.Max(0, p.PeakFromHour - 1))),
                PricingRules.BasePrice(p, RoomType.Standard, monday.AddHours(p.PeakFromHour)),
                PricingRules.BasePrice(p, RoomType.Standard, saturday.AddHours(p.PeakFromHour)),
                p.VipSurcharge, t.Id == chosen?.Id));
        }

        PriceMatrixModel? matrix = null;
        var promotions = new List<string>();
        if (chosen is not null)
        {
            if (pricings.TryGetValue(chosen.Id, out var pricing))
            {
                var present = await _db.Rooms.AsNoTracking().Where(r => r.TheaterId == chosen.Id)
                    .Select(r => r.RoomType).Distinct().ToListAsync(ct);
                matrix = PriceMatrixModel.Build(pricing, present.Count > 0 ? present : [RoomType.Standard]);
            }

            var today = _tz.ToLocalDate(_clock.GetUtcNow().UtcDateTime);
            var running = await _db.Promotions.AsNoTracking()
                .Where(p => p.TheaterId == chosen.Id && p.IsActive && p.StartDate <= today && p.EndDate >= today)
                .OrderBy(p => p.EndDate).ToListAsync(ct);
            promotions.AddRange(running.Select(p => p.AppliesAutomatically
                ? $"{p.Title}: {PromotionRules.Describe(p)}"
                : $"{p.Title}: {p.Description}"));
        }

        return View(new PricesViewModel(
            chosen is null ? null : new PricesBranchOption(chosen.Id, chosen.Name, chosen.City),
            chosen?.Address, chosen?.Phone, chosen?.OpeningHours, matrix, promotions, compare, options));
    }
}
