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
/// Quy ước cặp: ghế Couple có thể nằm ở BẤT KỲ hàng nào. Trong một hàng, các ghế Couple có số cột
/// liên tiếp tạo thành một "đoạn"; trong mỗi đoạn ghép từng đôi từ trái sang phải. Vì admin chỉ ghép
/// được từng cặp liền kề nên mọi đoạn đều có độ dài chẵn và cách ghép là duy nhất (không cần lưu
/// cặp trong DB). Ghế Couple lẻ loi (đoạn lẻ, hoặc bị khuyết ghế) không có cặp nên không bán được.
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

            // Quét trái → phải: gặp ghế kế bên liền kề thì ghép cả hai rồi nhảy 2; không thì ghế
            // này lẻ loi, nhảy 1. Khác với "cứ hai ghế một" — một ghế lẻ ở đầu hàng không làm lệch cả hàng.
            for (var i = 0; i < ordered.Count;)
            {
                if (i + 1 < ordered.Count && ordered[i].ColumnNumber + 1 == ordered[i + 1].ColumnNumber)
                {
                    partners[ordered[i].Id] = ordered[i + 1].Id;
                    partners[ordered[i + 1].Id] = ordered[i].Id;
                    i += 2;
                }
                else
                {
                    i += 1;
                }
            }
        }

        return partners;
    }

    /// <summary>Ghế Couple không có cặp (đoạn lẻ / khuyết ghế) — không bán được.</summary>
    public static IReadOnlyList<Seat> OrphanCoupleSeats(IEnumerable<Seat> seats)
    {
        var all = seats as IReadOnlyCollection<Seat> ?? seats.ToList();
        var partners = BuildPartnerMap(all);
        return all.Where(s => s.SeatType == SeatType.Couple && !partners.ContainsKey(s.Id)).ToList();
    }

    /// <summary>
    /// Kiểm sơ đồ ghế của một phòng: mọi ghế Couple phải có cặp. Trả về thông báo lỗi (nêu tên ghế
    /// lẻ loi) hoặc null nếu hợp lệ. Dùng khi admin lưu sơ đồ ghế; <paramref name="tolerated"/> là các
    /// ghế lẻ loi có sẵn từ trước (phòng cũ) — không chặn lưu vì chuyện đã tồn tại.
    /// </summary>
    public static string? ValidateLayout(IEnumerable<Seat> seats, IReadOnlySet<int>? tolerated = null)
    {
        var all = seats as IReadOnlyCollection<Seat> ?? seats.ToList();

        var orphans = OrphanCoupleSeats(all)
            .Where(s => tolerated is null || !tolerated.Contains(s.Id))
            .OrderBy(s => s.RowNumber).ThenBy(s => s.ColumnNumber)
            .Select(s => s.Label)
            .ToList();

        return orphans.Count == 0
            ? null
            : $"Ghế đôi phải đi thành cặp hai ghế liền kề cùng hàng. Ghế lẻ loi: {string.Join(", ", orphans)}.";
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
