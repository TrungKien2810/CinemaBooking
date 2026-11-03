using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Ghế đôi (Couple) bán theo CẶP: hai ghế liền kề cùng hàng, cả cặp là một chỗ ngồi đôi.
/// <para>
/// Vì sao phải ghép: nếu bán lẻ từng nửa thì khách trả tiền một ghế mà chiếm hai chỗ, còn nếu tính
/// mỗi nửa bằng hai vé thường thì cặp đôi thành bốn vé — cả hai đều sai. Giá đúng là: mỗi nửa bằng
/// một vé thường, cả cặp = hai vé thường.
/// </para>
/// <para>
/// Quy ước cặp: trong một hàng, duyệt từ trái sang phải, hai ghế Couple có số cột liên tiếp
/// (cột lẻ + cột chẵn kế tiếp) ghép thành một cặp. Ghế Couple lẻ loi (hàng có số ghế lẻ, hoặc bị
/// khuyết ghế) không có cặp hợp lệ nên không bán được.
/// </para>
/// </summary>
public static class SeatPairing
{
    /// <summary>seatId → seatId của nửa còn lại. Ghế không có cặp thì không có trong map.</summary>
    public static IReadOnlyDictionary<int, int> BuildPartnerMap(IEnumerable<Seat> seats)
    {
        var partners = new Dictionary<int, int>();

        foreach (var row in seats.Where(s => s.SeatType == SeatType.Couple).GroupBy(s => s.RowNumber))
        {
            var ordered = row.OrderBy(s => s.ColumnNumber).ToList();

            for (var i = 0; i + 1 < ordered.Count; i += 2)
            {
                var left = ordered[i];
                var right = ordered[i + 1];

                // Chỉ ghép khi thật sự liền kề; một ghế bị khuyết ở giữa thì hai bên không cùng cặp.
                if (left.ColumnNumber + 1 != right.ColumnNumber) continue;

                partners[left.Id] = right.Id;
                partners[right.Id] = left.Id;
            }
        }

        return partners;
    }

    /// <summary>
    /// Trả về thông báo lỗi nếu lựa chọn vi phạm quy tắc cặp; null nếu hợp lệ.
    /// Dùng ở cả controller (báo sớm cho khách) và <c>BookingService</c> (chốt chặn cuối).
    /// </summary>
    public static string? Validate(IEnumerable<Seat> allSeats, IReadOnlyCollection<int> selectedSeatIds)
    {
        var seats = allSeats as IReadOnlyCollection<Seat> ?? allSeats.ToList();
        var selected = selectedSeatIds.ToHashSet();
        var partners = BuildPartnerMap(seats);

        foreach (var seat in seats.Where(s => s.SeatType == SeatType.Couple && selected.Contains(s.Id)))
        {
            if (!partners.TryGetValue(seat.Id, out var partnerId))
                return $"Ghế đôi {seat.Label} không có cặp hợp lệ trong phòng chiếu.";

            if (selected.Contains(partnerId)) continue;

            var partner = seats.FirstOrDefault(s => s.Id == partnerId);
            return $"Ghế đôi phải chọn cả cặp: {seat.Label} + {partner?.Label ?? "?"}.";
        }

        return null;
    }
}
