using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Web.Services;

namespace CinemaBooking.Web.Models;

/// <summary>Dữ liệu trang chọn ghế của một suất chiếu. Giờ đã đổi sang giờ rạp.</summary>
public sealed record SeatMapViewModel(
    int ShowtimeId,
    string MovieTitle,
    string TheaterName,
    string City,
    string RoomName,
    DateTime StartLocal,
    int HoldSeconds,
    int TotalColumns,
    IReadOnlyList<SeatRowViewModel> Rows,
    string? FormatLabel = null)
{
    /// <summary>
    /// Dựng lưới ghế theo layout phòng: đủ <see cref="Room.TotalRows"/> hàng × <see cref="Room.TotalColumns"/> cột,
    /// ô không có ghế là null để CSS Grid vẫn thẳng cột. Cần <paramref name="showtime"/> đã nạp
    /// Movie, Room.Theater và Room.Seats.
    /// </summary>
    public static SeatMapViewModel Build(
        Showtime showtime,
        IReadOnlySet<int> bookedSeatIds,
        ISeatPricingService pricing,
        DateTime startLocal,
        int holdSeconds,
        IReadOnlyList<Promotion>? promotions = null,
        SeatSurcharges? surcharges = null)
    {
        var room = showtime.Room ?? throw new ArgumentException("Chưa nạp Room của suất chiếu.", nameof(showtime));
        var theater = room.Theater ?? throw new ArgumentException("Chưa nạp Room.Theater.", nameof(showtime));
        var movie = showtime.Movie ?? throw new ArgumentException("Chưa nạp Movie của suất chiếu.", nameof(showtime));

        var allSeats = room.Seats.ToList();
        var byPosition = allSeats.ToDictionary(s => (s.RowNumber, s.ColumnNumber));
        var byId = allSeats.ToDictionary(s => s.Id);

        // Ghế đôi hiển thị thành MỘT ô rộng 2 cột (hình chữ nhật), không phải hai ô rời.
        var partners = SeatPairing.BuildPartnerMap(allSeats);

        var rows = new List<SeatRowViewModel>(room.TotalRows);
        for (var row = 1; row <= room.TotalRows; row++)
        {
            var cells = new SeatCellViewModel?[room.TotalColumns];
            for (var col = 1; col <= room.TotalColumns; col++)
            {
                if (!byPosition.TryGetValue((row, col), out var seat)) continue;

                if (seat.SeatType == SeatType.Couple && partners.TryGetValue(seat.Id, out var partnerId))
                {
                    var partner = byId[partnerId];

                    // Ghép tại nửa bên trái; nửa bên phải là ô bị che, view bỏ qua khi render
                    // (vẫn giữ phần tử để số cột của hàng không đổi).
                    var pairQuote = PromotionRules.Quote(
                        pricing.PriceFor(showtime.BasePrice, SeatType.Couple, surcharges), SeatType.Couple, startLocal, promotions);
                    cells[col - 1] = partner.ColumnNumber > seat.ColumnNumber
                        ? new SeatCellViewModel(
                            [seat.Id, partner.Id],
                            $"{seat.Label}–{partner.Label}",
                            SeatType.Couple,
                            pairQuote.FinalPrice * 2,
                            bookedSeatIds.Contains(seat.Id) || bookedSeatIds.Contains(partner.Id),
                            ListPrice: pairQuote.ListPrice * 2,
                            PromotionTitle: pairQuote.PromotionTitle)
                        : SeatCellViewModel.CoveredByPair;
                    continue;
                }

                // Ghế đôi lẻ loi (hàng lẻ ghế / khuyết ghế): không bán được vì không có cặp.
                var quote = PromotionRules.Quote(
                    pricing.PriceFor(showtime.BasePrice, seat.SeatType, surcharges), seat.SeatType, startLocal, promotions);
                cells[col - 1] = new SeatCellViewModel(
                    [seat.Id],
                    seat.Label,
                    seat.SeatType,
                    quote.FinalPrice,
                    bookedSeatIds.Contains(seat.Id),
                    IsOrphan: seat.SeatType == SeatType.Couple,
                    ListPrice: quote.ListPrice,
                    PromotionTitle: quote.PromotionTitle);
            }
            rows.Add(new SeatRowViewModel(RowLabel(row), cells));
        }

        return new SeatMapViewModel(
            showtime.Id, movie.Title, theater.Name, theater.City, room.RoomName,
            startLocal, holdSeconds, room.TotalColumns, rows,
            FormatLabel: room.RoomType.Label() + " · " + showtime.Language.Label());
    }

    private static string RowLabel(int row) => ((char)('A' + row - 1)).ToString();
}

/// <summary>Một hàng ghế; <see cref="Seats"/> có đúng TotalColumns phần tử, null là lối đi/ô trống.</summary>
public sealed record SeatRowViewModel(string Label, IReadOnlyList<SeatCellViewModel?> Seats);

/// <summary>
/// Một ô trên sơ đồ. Ghế đơn có 1 id; <b>ghế đôi có 2 id</b> và là một khối duy nhất (giá = cả cặp).
/// <see cref="IsCoveredByPair"/> đánh dấu nửa phải của cặp — giữ chỗ trong lưới nhưng không render.
/// </summary>
public sealed record SeatCellViewModel(
    IReadOnlyList<int> SeatIds,
    string Label,
    SeatType Type,
    decimal Price,
    bool IsBooked,
    bool IsCoveredByPair = false,
    bool IsOrphan = false,
    decimal ListPrice = 0m,
    string? PromotionTitle = null)
{
    /// <summary>Đang có khuyến mãi: <see cref="Price"/> là giá đã giảm, <see cref="ListPrice"/> là giá gốc.</summary>
    public bool HasPromotion => PromotionTitle is not null && ListPrice > Price;

    /// <summary>Nửa phải của một cặp ghế đôi: đã được vẽ chung với nửa trái.</summary>
    public static SeatCellViewModel CoveredByPair { get; } =
        new(Array.Empty<int>(), string.Empty, SeatType.Couple, 0m, false, IsCoveredByPair: true);

    public bool IsPair => SeatIds.Count > 1;
}
