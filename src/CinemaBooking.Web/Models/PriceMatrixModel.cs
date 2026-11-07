using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Web.Models;

/// <summary>Một dòng của bảng giá: kịch bản (ngày + khung giờ) và giá từng loại ghế.</summary>
public sealed record PriceScenarioRow(string Label, string Detail, bool IsPeak, bool IsWeekend, decimal Standard, decimal Vip, decimal CoupleSeat)
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

        // Phụ thu = 0 nghĩa là chi nhánh tắt khoản đó: bảng chỉ còn các dòng thật sự khác giá nhau.
        var peakOn = PricingRules.PeakEnabled(p);
        var weekendOn = PricingRules.WeekendEnabled(p);
        var list = new List<(string Label, string Detail, bool Peak, bool Weekend, DateTime Start)>();
        if (peakOn && weekendOn)
        {
            list.Add(("Ngày thường", $"Thứ 2 – Thứ 6, suất bắt đầu trước {peak}:00", false, false, monday.AddHours(dayHour)));
            list.Add(("Ngày thường · giờ cao điểm", $"Thứ 2 – Thứ 5, suất bắt đầu từ {peak}:00", true, false, monday.AddHours(peak)));
            list.Add(("Cuối tuần", $"Thứ 7, Chủ nhật, suất bắt đầu trước {peak}:00", false, true, saturday.AddHours(dayHour)));
            list.Add(("Cuối tuần · giờ cao điểm", $"Thứ 6 từ {peak}:00, Thứ 7, Chủ nhật, suất bắt đầu từ {peak}:00", true, true, saturday.AddHours(peak)));
        }
        else if (peakOn)
        {
            list.Add(("Giờ thường", $"Mọi ngày, suất bắt đầu trước {peak}:00", false, false, monday.AddHours(dayHour)));
            list.Add(("Giờ cao điểm", $"Mọi ngày, suất bắt đầu từ {peak}:00", true, false, monday.AddHours(peak)));
        }
        else if (weekendOn)
        {
            list.Add(("Ngày thường", $"Thứ 2 – Thứ 5, và Thứ 6 trước {peak}:00", false, false, monday.AddHours(dayHour)));
            list.Add(("Cuối tuần", $"Thứ 6 từ {peak}:00, Thứ 7, Chủ nhật", false, true, saturday.AddHours(peak)));
        }
        else
        {
            list.Add(("Mọi ngày, mọi giờ chiếu", "Không có phụ thu giờ cao điểm hay cuối tuần", false, false, monday.AddHours(dayHour)));
        }
        var scenarios = list;

        var tables = roomTypes.Distinct().OrderBy(t => t).Select(t => new RoomPriceTable(t,
            scenarios.Select(s => new PriceScenarioRow(
                s.Label, s.Detail, s.Peak, s.Weekend,
                PricingRules.SeatPrice(p, t, SeatType.Standard, s.Start),
                PricingRules.SeatPrice(p, t, SeatType.Vip, s.Start),
                PricingRules.SeatPrice(p, t, SeatType.Couple, s.Start))).ToList())).ToList();

        return new PriceMatrixModel(p, tables);
    }
}
