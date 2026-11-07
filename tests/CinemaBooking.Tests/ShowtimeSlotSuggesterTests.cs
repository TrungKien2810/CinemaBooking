using CinemaBooking.Web.Services;

namespace CinemaBooking.Tests;

public class ShowtimeSlotSuggesterTests
{
    private static readonly DateOnly Day = new(2026, 12, 1);
    private static readonly TimeSpan Buffer = TimeSpan.FromMinutes(15);
    private static DateTime At(int h, int m = 0) => Day.ToDateTime(new TimeOnly(h, m));

    [Fact]
    public void Empty_room_offers_half_hour_grid_from_opening()
    {
        var slots = ShowtimeSlotSuggester.Suggest(Day, 120, [], Buffer);

        Assert.Equal(At(8), slots.First().StartLocal);
        Assert.Equal(At(23, 30), slots.Last().StartLocal);
        Assert.Equal(At(12), slots[4].EndLocal); // slot 10:00 + 120 phút
    }

    [Fact]
    public void Never_suggests_a_slot_clashing_with_an_existing_showtime_or_its_cleaning_buffer()
    {
        var existing = new[] { (At(10), At(12)) };

        var slots = ShowtimeSlotSuggester.Suggest(Day, 120, existing, Buffer);

        Assert.All(slots, s => Assert.False(s.StartLocal < At(12, 15) && s.EndLocal > At(9, 45)));
    }

    [Fact]
    public void Marks_the_slot_right_after_the_previous_showtime_as_snug()
    {
        var existing = new[] { (At(10), At(11, 58)) };

        var slots = ShowtimeSlotSuggester.Suggest(Day, 100, existing, Buffer);

        var snug = Assert.Single(slots, s => s.Snug);
        Assert.Equal(At(12, 15), snug.StartLocal); // 11:58 + 15' = 12:13 → làm tròn 12:15
    }

    [Fact]
    public void Skips_times_before_now()
    {
        var slots = ShowtimeSlotSuggester.Suggest(Day, 90, [], Buffer, notBeforeLocal: At(14, 10));

        Assert.All(slots, s => Assert.True(s.StartLocal >= At(14, 10)));
        Assert.Equal(At(14, 30), slots.First().StartLocal);
    }

    [Fact]
    public void Handles_a_showtime_running_past_midnight_from_the_previous_day()
    {
        var existing = new[] { (Day.AddDays(-1).ToDateTime(new TimeOnly(22, 0)), At(0, 40)) };

        var slots = ShowtimeSlotSuggester.Suggest(Day, 90, existing, Buffer);

        Assert.Equal(At(8), slots.First().StartLocal);
    }
}
