namespace CinemaBooking.Web.Models;

/// <summary>Một dòng của bảng so sánh giá giữa các chi nhánh (giá ghế thường phòng 2D).</summary>
public sealed record BranchPriceSummary(
    int Id, string Name, string City, decimal WeekdayDay, decimal WeekdayPeak, decimal WeekendPeak, decimal VipSurcharge, bool IsSelected);

public sealed record PricesBranchOption(int Id, string Name, string City);

/// <summary>Trang "Bảng giá vé" của khách: bảng giá chi tiết một chi nhánh + so sánh các chi nhánh.</summary>
public sealed record PricesViewModel(
    PricesBranchOption? Selected,
    string? Address,
    string? Phone,
    string? OpeningHours,
    PriceMatrixModel? Matrix,
    IReadOnlyList<string> Promotions,
    IReadOnlyList<BranchPriceSummary> Compare,
    IReadOnlyList<PricesBranchOption> Branches)
{
    public bool HasPricing => Matrix is not null;
}
