using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Web.Services;

namespace CinemaBooking.Tests;

/// <summary>
/// Ghế đôi chỉ bán theo cặp. Test khóa quy ước ghép (liền kề, trái→phải) và thông báo lỗi
/// khi khách chọn lẻ một nửa — nếu quy tắc này lỏng, khách trả một vé mà chiếm hai chỗ.
/// </summary>
public class SeatPairingTests
{
    private static List<Seat> Row(int rowNumber, int count, SeatType type = SeatType.Couple)
    {
        var seats = new List<Seat>(count);
        for (var col = 1; col <= count; col++)
        {
            seats.Add(new Seat
            {
                Id = rowNumber * 100 + col,
                RowNumber = rowNumber,
                ColumnNumber = col,
                SeatType = type,
            });
        }
        return seats;
    }

    [Fact]
    public void Pairs_adjacent_couple_seats_left_to_right()
    {
        var seats = Row(rowNumber: 1, count: 14);

        var partners = SeatPairing.BuildPartnerMap(seats);

        Assert.Equal(14, partners.Count);
        Assert.Equal(102, partners[101]);
        Assert.Equal(101, partners[102]);
        Assert.Equal(114, partners[113]);
        Assert.Equal(113, partners[114]);
    }

    [Fact]
    public void Non_adjacent_couple_seats_do_not_form_a_pair()
    {
        var seats = new List<Seat>
        {
            new() { Id = 1, RowNumber = 1, ColumnNumber = 1, SeatType = SeatType.Couple },
            new() { Id = 2, RowNumber = 1, ColumnNumber = 3, SeatType = SeatType.Couple },
        };

        Assert.Empty(SeatPairing.BuildPartnerMap(seats));
        Assert.NotNull(SeatPairing.Validate(seats, new[] { 1 }));
    }

    [Fact]
    public void Half_a_pair_is_rejected_and_the_message_names_the_partner()
    {
        var seats = Row(rowNumber: 1, count: 2);

        var error = SeatPairing.Validate(seats, new[] { 101 });

        Assert.NotNull(error);
        Assert.Contains("cả cặp", error);
        Assert.Contains("A1", error);
        Assert.Contains("A2", error);
    }

    [Fact]
    public void Full_pair_is_accepted()
    {
        var seats = Row(rowNumber: 1, count: 2);

        Assert.Null(SeatPairing.Validate(seats, new[] { 101, 102 }));
    }

    [Fact]
    public void Standard_seats_are_not_subject_to_the_pair_rule()
    {
        var seats = Row(rowNumber: 1, count: 2, SeatType.Standard);

        Assert.Null(SeatPairing.Validate(seats, new[] { 101 }));
    }
}
