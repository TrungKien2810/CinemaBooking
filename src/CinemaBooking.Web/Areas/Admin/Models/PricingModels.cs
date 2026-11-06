using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Web.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CinemaBooking.Web.Areas.Admin.Models;

/// <summary>Form bảng giá của một chi nhánh. Ô giá gõ theo nghìn đồng (gõ 95 = 95.000đ).</summary>
public sealed class PricingFormModel
{
    public int TheaterId { get; set; }

    [ValidateNever] public string TheaterName { get; set; } = string.Empty;

    private decimal _standard = 80_000m, _threeD = 100_000m, _imax = 130_000m, _fourDx = 150_000m;
    private decimal _vip = 20_000m, _couple = 10_000m, _peak = 20_000m, _weekend = 10_000m;

    private const string RoomRange = "Giá phòng từ 10.000đ đến 2.000.000đ (gõ 95 = 95.000đ).";
    private const string SurchargeRange = "Phụ thu từ 0 đến 1.000.000đ (gõ 20 = 20.000đ).";

    [Range(10_000, 2_000_000, ErrorMessage = RoomRange)]
    public decimal StandardPrice { get => _standard; set => _standard = MoneyFormat.ExpandThousands(value); }

    [Range(10_000, 2_000_000, ErrorMessage = RoomRange)]
    public decimal ThreeDPrice { get => _threeD; set => _threeD = MoneyFormat.ExpandThousands(value); }

    [Range(10_000, 2_000_000, ErrorMessage = RoomRange)]
    public decimal ImaxPrice { get => _imax; set => _imax = MoneyFormat.ExpandThousands(value); }

    [Range(10_000, 2_000_000, ErrorMessage = RoomRange)]
    public decimal FourDxPrice { get => _fourDx; set => _fourDx = MoneyFormat.ExpandThousands(value); }

    [Range(0, 1_000_000, ErrorMessage = SurchargeRange)]
    public decimal VipSurcharge { get => _vip; set => _vip = MoneyFormat.ExpandThousands(value); }

    [Range(0, 1_000_000, ErrorMessage = SurchargeRange)]
    public decimal CoupleSurcharge { get => _couple; set => _couple = MoneyFormat.ExpandThousands(value); }

    [Range(0, 1_000_000, ErrorMessage = SurchargeRange)]
    public decimal PeakSurcharge { get => _peak; set => _peak = MoneyFormat.ExpandThousands(value); }

    [Range(0, 1_000_000, ErrorMessage = SurchargeRange)]
    public decimal WeekendSurcharge { get => _weekend; set => _weekend = MoneyFormat.ExpandThousands(value); }

    /// <summary>Công tắc: bỏ chọn = không thu phụ thu (lưu 0).</summary>
    public bool PeakEnabled { get; set; } = true;
    public bool WeekendEnabled { get; set; } = true;

    [Display(Name = "Giờ cao điểm bắt đầu từ")]
    [Range(10, 23, ErrorMessage = "Giờ cao điểm từ 10 đến 23 giờ.")]
    public int PeakFromHour { get; set; } = 17;

    [ValidateNever] public List<SelectListItem> Theaters { get; set; } = [];

    /// <summary>Chỉ để vẽ bản xem trước; không nhận từ request.</summary>
    [ValidateNever] public PriceMatrixModel? Preview { get; set; }

    public static PricingFormModel From(TheaterPricing p, string theaterName) => new()
    {
        TheaterId = p.TheaterId, TheaterName = theaterName,
        StandardPrice = p.StandardPrice, ThreeDPrice = p.ThreeDPrice, ImaxPrice = p.ImaxPrice, FourDxPrice = p.FourDxPrice,
        VipSurcharge = p.VipSurcharge, CoupleSurcharge = p.CoupleSurcharge,
        PeakSurcharge = p.PeakSurcharge, WeekendSurcharge = p.WeekendSurcharge, PeakFromHour = p.PeakFromHour,
        PeakEnabled = p.PeakSurcharge > 0m, WeekendEnabled = p.WeekendSurcharge > 0m,
    };

    public void ApplyTo(TheaterPricing p)
    {
        p.StandardPrice = StandardPrice; p.ThreeDPrice = ThreeDPrice; p.ImaxPrice = ImaxPrice; p.FourDxPrice = FourDxPrice;
        p.VipSurcharge = VipSurcharge; p.CoupleSurcharge = CoupleSurcharge;
        p.PeakSurcharge = PeakEnabled ? PeakSurcharge : 0m; p.WeekendSurcharge = WeekendEnabled ? WeekendSurcharge : 0m; p.PeakFromHour = PeakFromHour;
    }

    public TheaterPricing ToPricing() { var p = new TheaterPricing { TheaterId = TheaterId }; ApplyTo(p); return p; }
}
