using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web;
using CinemaBooking.Web.Areas.Admin.Controllers;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

public class TextSearchTests
{
    [Theory]
    [InlineData("can tho", "CineSmart Cần Thơ", true)]
    [InlineData("CẦN THƠ", "cineSmart can tho", true)]
    [InlineData("dien bien", "25 Điện Biên Phủ", true)]
    [InlineData("long bien ha noi", "CineSmart Long Biên", false)]   // thiếu "ha noi" trong chuỗi này
    [InlineData("", "bất kỳ", true)]
    [InlineData(null, "bất kỳ", true)]
    [InlineData("đà nẵng", "Da Nang", true)]
    public void Matches_ignores_case_and_vietnamese_accents(string? query, string text, bool expected)
    {
        Assert.Equal(expected, TextSearch.Matches(query, text));
    }

    [Fact]
    public void Every_word_must_appear_somewhere_across_the_fields()
    {
        Assert.True(TextSearch.Matches("long bien ha noi", "CineSmart Long Biên", "27 Cổ Linh", "Hà Nội"));
        Assert.False(TextSearch.Matches("long bien hue", "CineSmart Long Biên", "27 Cổ Linh", "Hà Nội"));
    }
}

public class AisleLimitTests
{
    private static List<ValidationResult> Validate(int columns, int aisle)
    {
        var form = new RoomFormModel { RoomName = "P", TotalRows = 5, TotalColumns = columns, AisleAfterColumn = aisle };
        return form.Validate(new ValidationContext(form)).ToList();
    }

    [Theory]
    [InlineData(10, 0)]   // không lối đi
    [InlineData(9, 2)]    // nhỏ nhất: mỗi bên ≥ 2 ghế
    [InlineData(9, 6)]    // lớn nhất = cột − 3
    [InlineData(5, 2)]    // phòng nhỏ nhất có lối đi
    [InlineData(19, 9)]
    public void Valid_aisle_positions_pass(int columns, int aisle) => Assert.Empty(Validate(columns, aisle));

    [Theory]
    [InlineData(9, 1)]    // bên trái chỉ 1 ghế
    [InlineData(9, 7)]    // bên phải chỉ 1 ghế
    [InlineData(9, 8)]
    [InlineData(9, 20)]
    [InlineData(4, 2)]    // phòng < 5 cột không chừa được
    [InlineData(10, -1)]
    public void Aisles_that_leave_fewer_than_two_seats_on_a_side_are_rejected(int columns, int aisle)
    {
        var errors = Validate(columns, aisle);

        Assert.Single(errors);
        Assert.Contains(nameof(RoomFormModel.AisleAfterColumn), errors[0].MemberNames);
    }

    [Fact]
    public void Error_message_tells_the_admin_the_allowed_range()
    {
        var message = Validate(12, 11).Single().ErrorMessage!;

        Assert.Contains("2", message);
        Assert.Contains("9", message); // 12 − 3
    }

    [Fact]
    public void Generator_and_preview_ignore_an_out_of_range_aisle_instead_of_drawing_a_broken_room()
    {
        var seats = SeatGenerator.Generate(new Room { Id = 1, TotalRows = 3, TotalColumns = 9 }, SeatLayoutPreset.AllStandard, aisleAfterColumn: 8);
        var preview = SeatPreviewModel.For(3, 9, SeatLayoutPreset.AllStandard, aisleAfterColumn: 8);

        Assert.Equal(27, seats.Count);
        Assert.False(preview.HasAisle);
        Assert.Equal(27, SeatGenerator.SeatCount(3, 9, 8));
        Assert.Equal((2, 6), SeatGenerator.AisleRange(9));
        Assert.Null(SeatGenerator.AisleRange(4));
    }
}

public class VndShortTests
{
    [Theory]
    [InlineData(2_074_750_000, "2,07 tỷ đ")]
    [InlineData(1_000_000_000, "1 tỷ đ")]
    [InlineData(337_631_000, "337,6 tr đ")]
    [InlineData(1_525_000, "1,5 tr đ")]
    [InlineData(450_000, "450.000 đ")]
    [InlineData(0, "0 đ")]
    public void Large_amounts_are_shortened_for_narrow_cards(long amount, string expected)
    {
        Assert.Equal(expected, ((decimal)amount).VndShort());
    }

    [Fact]
    public void The_unit_never_wraps_onto_its_own_line()
    {
        Assert.DoesNotContain(' ', 90_000m.Vnd());
        Assert.DoesNotContain(' ', 2_074_750_000m.VndShort());
    }
}

public class AdminListFilterTests
{
    private static ShowtimesController NoUse = null!;

    [Fact]
    public async Task Theaters_can_be_searched_without_typing_accents_and_filtered_by_status()
    {
        var db = TestDatabase.Create();
        db.Theaters.AddRange(
            new Theater { Name = "CineSmart Cần Thơ", City = "Cần Thơ", Address = "209 Đường 30/4" },
            new Theater { Name = "CineSmart Cầu Giấy", City = "Hà Nội", IsActive = false },
            new Theater { Name = "CineSmart Đà Nẵng", City = "Đà Nẵng" });
        await db.SaveChangesAsync();
        var controller = new TheatersController(db);

        var byName = Assert.IsType<TheatersIndexViewModel>(Assert.IsType<ViewResult>(await controller.Index("can tho", null, null, default)).Model);
        var closed = Assert.IsType<TheatersIndexViewModel>(Assert.IsType<ViewResult>(await controller.Index(null, null, "closed", default)).Model);
        var byCity = Assert.IsType<TheatersIndexViewModel>(Assert.IsType<ViewResult>(await controller.Index(null, "đà nẵng", null, default)).Model);
        var junkCity = Assert.IsType<TheatersIndexViewModel>(Assert.IsType<ViewResult>(await controller.Index(null, "Atlantis", "bogus", default)).Model);

        Assert.Equal(["CineSmart Cần Thơ"], byName.Items.Select(t => t.Name));
        Assert.Equal(["CineSmart Cầu Giấy"], closed.Items.Select(t => t.Name));
        Assert.Equal(["CineSmart Đà Nẵng"], byCity.Items.Select(t => t.Name));
        // Giá trị lạ trên URL bị bỏ qua thay vì làm trống danh sách.
        Assert.Equal(3, junkCity.Items.Count);
        Assert.Equal(3, byName.Total);
    }

    [Fact]
    public async Task Movies_can_be_searched_by_director_and_filtered_by_status_and_genre()
    {
        var db = TestDatabase.Create();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        db.Movies.AddRange(
            new Movie { Title = "Mắt Biếc", Genre = "Tình cảm", Director = "Victor Vũ", DurationMinutes = 117, ReleaseDate = today.AddDays(-100) },
            new Movie { Title = "Mùa Hè Rực Rỡ", Genre = "Tình cảm", Director = "Victor Vũ", DurationMinutes = 112, ReleaseDate = today.AddDays(20) },
            new Movie { Title = "Cám", Genre = "Kinh dị", DurationMinutes = 122, ReleaseDate = today.AddDays(-10), IsActive = false });
        await db.SaveChangesAsync();
        var controller = new CinemaBooking.Web.Areas.Admin.Controllers.MoviesController(db);

        MoviesIndexViewModel Run(string? q, string? status, string? genre) =>
            Assert.IsType<MoviesIndexViewModel>(Assert.IsType<ViewResult>(controller.Index(q, status, genre, default).GetAwaiter().GetResult()).Model);

        Assert.Equal(2, Run("victor vu", null, null).Items.Count);
        Assert.Equal(["Mùa Hè Rực Rỡ"], Run(null, "soon", null).Items.Select(m => m.Title));
        Assert.Equal(["Mắt Biếc"], Run(null, "active", null).Items.Select(m => m.Title));
        Assert.Equal(["Cám"], Run(null, "hidden", null).Items.Select(m => m.Title));
        Assert.Equal(["Cám"], Run(null, null, "kinh dị").Items.Select(m => m.Title));
        Assert.Equal(3, Run(null, "bogus", "bogus").Items.Count);
    }

    [Fact]
    public async Task Bookings_are_paged_and_searchable_by_name_email_phone_and_date()
    {
        var db = TestDatabase.Create();
        var room = db.SeedRoom();
        var movie = db.SeedMovie("Mắt Biếc");
        await db.SaveChangesAsync();
        var showtime = db.SeedShowtime(movie, room, new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        for (var i = 1; i <= 45; i++)
        {
            db.Bookings.Add(new Booking
            {
                BookingCode = $"CB-T{i:000}", ShowtimeId = showtime.Id, CustomerName = i == 7 ? "Nguyễn Văn Đặc Biệt" : $"Khách {i}",
                CustomerEmail = i == 8 ? "hiem.co@example.com" : $"k{i}@example.com", CustomerPhone = i == 9 ? "0988777666" : "0900000000",
                BookingTimeUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i - 1), Status = BookingStatus.Confirmed,
            });
        }
        await db.SaveChangesAsync();
        var controller = new BookingsController(db, Options.Create(new DisplayTimeZone()), FakeBranchScope.Admin());

        BookingsIndexViewModel Run(string? q = null, string? code = null, int? page = null, DateOnly? from = null, DateOnly? to = null) =>
            Assert.IsType<BookingsIndexViewModel>(Assert.IsType<ViewResult>(
                controller.Index(q, code, null, null, from, to, page, default).GetAwaiter().GetResult()).Model);

        var first = Run();
        Assert.Equal(40, first.Items.Count);
        Assert.Equal(45, first.Paging!.Total);
        Assert.Equal(2, first.Paging.TotalPages);
        Assert.Equal(5, Run(page: 2).Items.Count);
        Assert.Equal(5, Run(page: 99).Items.Count);          // trang quá lớn được kẹp lại
        Assert.Equal(40, Run(page: -3).Items.Count);

        Assert.Equal("CB-T007", Run("Đặc Biệt").Items.Single().BookingCode);
        Assert.Equal("CB-T008", Run("hiem.co").Items.Single().BookingCode);
        Assert.Equal("CB-T009", Run("0988777666").Items.Single().BookingCode);
        Assert.Equal("CB-T010", Run(code: "t010").Items.Single().BookingCode); // tham số cũ "code" vẫn chạy

        var october = Run(from: new DateOnly(2026, 10, 1), to: new DateOnly(2026, 10, 3));
        Assert.Equal(3, october.Items.Count);
        Assert.Equal(3, Run(from: new DateOnly(2026, 10, 3), to: new DateOnly(2026, 10, 1)).Items.Count); // đảo ngược vẫn đúng
        Assert.True(first.IsFiltered == false && october.IsFiltered);
    }
}
