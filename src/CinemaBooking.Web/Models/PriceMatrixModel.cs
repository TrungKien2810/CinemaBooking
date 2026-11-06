using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Web.Models;

/// <summary>Một dòng của bảng giá: kịch bản (ngày + khung giờ) và giá từng loại ghế.</summary>
public sealed record PriceScenarioRow(string Label, decimal Standard, decimal Vip, decimal CoupleSeat)
{
    /// <summary>Ghế đôi bán theo cặp: giá một cặp = 2 ghế.</summary>
    public decimal CouplePair => CoupleSeat * 2;
}

/// <summary>Bảng giá của một loại phòng: bốn kịch bản (ngày thường/cuối tuần × trước/từ giờ cao điểm).</summary>
public sealed record RoomPriceTable(RoomType Type, IReadOnlyList<PriceScenarioRow> Rows);

/// <summary>Dữ liệu cho partial <c>_PriceMatrix</c> — dùng ở trang bảng giá của khách và bản xem trước của admin.</summary>
public sealed record PriceMatrixModel(TheaterPricing Pricing, IReadOnlyList<RoomPriceTable> Tables)
{
    /// <summary>Dựng bảng giá cho các loại phòng cho trước bằng ĐÚNG công thức tính giá thật (<see cref="PricingRules"/>).</summary>
    public static PriceMatrixModel Build(TheaterPricing p, IEnumerable<RoomType> roomTypes)
    {
        var peak = p.PeakFromHour;
        var dayHour = Math.Max(0, peak - 1);   // giờ ngay trước giờ cao điểm
        // 2026-11-02 là thứ Hai, 2026-11-07 là thứ Bảy — chỉ dùng để chọn đúng "loại ngày".
        var monday = new DateTime(2026, 11, 2);
        var saturday = new DateTime(2026, 11, 7);

        var scenarios = new (string Label, DateTime Start)[]
        {
            ($"Thứ 2 – Thứ 5 · trước {peak}:00", monday.AddHours(dayHour)),
            ($"Thứ 2 – Thứ 5 · từ {peak}:00", monday.AddHours(peak)),
            ($"Thứ 6 (từ {peak}:00), Thứ 7, Chủ nhật · trước {peak}:00", saturday.AddHours(dayHour)),
            ($"Thứ 6 (từ {peak}:00), Thứ 7, Chủ nhật · từ {peak}:00", saturday.AddHours(peak)),
        };

        var tables = roomTypes.Distinct().OrderBy(t => t).Select(t => new RoomPriceTable(t,
            scenarios.Select(s => new PriceScenarioRow(
                s.Label,
                PricingRules.SeatPrice(p, t, SeatType.Standard, s.Start),
                PricingRules.SeatPrice(p, t, SeatType.Vip, s.Start),
                PricingRules.SeatPrice(p, t, SeatType.Couple, s.Start))).ToList())).ToList();

        return new PriceMatrixModel(p, tables);
    }
}
