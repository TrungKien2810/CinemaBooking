using CinemaBooking.Domain.Enums;
using CinemaBooking.Web.Areas.Admin.Models;

namespace CinemaBooking.Tests;

public class SeatPreviewModelTests
{
    [Fact]
    public void For_builds_one_row_per_seat_row_in_column_order()
    {
        var preview = SeatPreviewModel.For(8, 12);

        Assert.Equal(8, preview.Grid.Count);
        Assert.All(preview.Grid, row => Assert.Equal(12, row.Count));
        Assert.Equal("A", SeatPreviewModel.RowLabel(0));
        Assert.Equal("H", SeatPreviewModel.RowLabel(7));
    }

    [Fact]
    public void For_matches_the_seat_types_the_generator_saves()
    {
        var preview = SeatPreviewModel.For(8, 2);

        Assert.All(preview.Grid[7], t => Assert.Equal(SeatType.Couple, t));
        Assert.All(preview.Grid[3], t => Assert.Equal(SeatType.Vip, t));
        Assert.All(preview.Grid[0], t => Assert.Equal(SeatType.Standard, t));
        Assert.Equal(16, preview.Count(SeatType.Standard) + preview.Count(SeatType.Vip) + preview.Count(SeatType.Couple));
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, 10, 1, 10)]
    [InlineData(40, 99, SeatPreviewModel.MaxRows, SeatPreviewModel.MaxColumns)]
    public void For_clamps_half_typed_or_out_of_range_input(int rows, int cols, int expectedRows, int expectedCols)
    {
        var preview = SeatPreviewModel.For(rows, cols);

        Assert.Equal(expectedRows, preview.Rows);
        Assert.Equal(expectedCols, preview.Columns);
        Assert.Equal(expectedRows, preview.Grid.Count);
    }
}
