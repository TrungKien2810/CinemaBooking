using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Controllers;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

/// <summary>Quy tắc bảo vệ vé đã bán và luồng rạp hủy vé.</summary>
public class SoldTicketRulesTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StartUtc = NowUtc.AddDays(2);

    private static async Task<(CinemaDbContext Db, Movie Movie, Room Room, Showtime Showtime)> SeedAsync()
    {
        var db = TestDatabase.Create();
        var theater = new Theater { Name = "CGV Test", City = "Hà Nội", Address = "1 Test" };
        var room = new Room { Theater = theater, RoomName = "Phòng 1", TotalRows = 1, TotalColumns = 2 };
        room.Seats.Add(new Seat { RowNumber = 1, ColumnNumber = 1, SeatType = SeatType.Standard });
        room.Seats.Add(new Seat { RowNumber = 1, ColumnNumber = 2, SeatType = SeatType.Standard });
        var movie = new Movie { Title = "Phim A", DurationMinutes = 100, ReleaseDate = new DateOnly(2026, 1, 1), IsActive = true };
        var other = new Movie { Title = "Phim B", DurationMinutes = 100, ReleaseDate = new DateOnly(2026, 1, 1), IsActive = true };
        var showtime = new Showtime
        {
            Movie = movie, Room = room, StartTimeUtc = StartUtc, EndTimeUtc = StartUtc.AddMinutes(100),
            BasePrice = 90_000m, IsActive = true,
        };
        db.AddRange(theater, movie, other, showtime);
        await db.SaveChangesAsync();
        return (db, movie, room, showtime);
    }

    private static async Task<Booking> SellAsync(CinemaDbContext db, Showtime showtime, Room room, BookingStatus status = BookingStatus.Confirmed)
    {
        var seat = await db.Seats.FirstAsync(s => s.RoomId == room.Id && !db.BookingSeats.Any(b => b.SeatId == s.Id));
        var booking = new Booking
        {
            BookingCode = "CB-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            ShowtimeId = showtime.Id, CustomerName = "Khách", CustomerEmail = "khach@example.com",
            BookingTimeUtc = NowUtc, TotalAmount = 90_000m, Status = status,
            Payment = new Payment { Amount = 90_000m, PaymentMethod = "VNPAY", Status = PaymentStatus.Succeeded },
        };
        booking.Seats.Add(new BookingSeat { SeatId = seat.Id, ShowtimeId = showtime.Id, PriceAtBooking = 90_000m });
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking;
    }

    private static TimeProvider Clock() => new FixedTimeProvider(NowUtc);

    private static BookingCancellationService Cancellation(CinemaDbContext db) => new(db, Clock(), new DisplayTimeZone());

    private static ShowtimesController Showtimes(CinemaDbContext db) => new(
        db, new ShowtimeScheduler(db), Options.Create(new DisplayTimeZone()), Clock(), FakeBranchScope.Admin(), Cancellation(db));

    private static ShowtimeFormModel Form(Showtime s) => new()
    {
        MovieId = s.MovieId, RoomId = s.RoomId, StartTimeLocal = s.StartTimeUtc.AddHours(7), Language = s.Language,
    };

    [Fact]
    public async Task Sold_showtime_keeps_its_movie()
    {
        var (db, movie, room, showtime) = await SeedAsync();
        await SellAsync(db, showtime, room);
        var otherMovie = await db.Movies.SingleAsync(m => m.Id != movie.Id);
        var controller = Showtimes(db);
        var form = Form(showtime);
        form.MovieId = otherMovie.Id;

        var result = await controller.Edit(showtime.Id, form, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(form.MovieId)));
        Assert.Equal(movie.Id, (await db.Showtimes.AsNoTracking().SingleAsync()).MovieId);
    }

    [Fact]
    public async Task Sold_showtime_keeps_its_start_time()
    {
        var (db, _, room, showtime) = await SeedAsync();
        await SellAsync(db, showtime, room);
        var controller = Showtimes(db);
        var form = Form(showtime);
        form.StartTimeLocal = form.StartTimeLocal!.Value.AddHours(3);

        var result = await controller.Edit(showtime.Id, form, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(StartUtc, (await db.Showtimes.AsNoTracking().SingleAsync()).StartTimeUtc);
    }

    [Fact]
    public async Task Sold_showtime_can_still_change_language()
    {
        var (db, _, room, showtime) = await SeedAsync();
        await SellAsync(db, showtime, room);
        var form = Form(showtime);
        form.Language = ShowLanguage.Dubbed;

        var result = await Showtimes(db).Edit(showtime.Id, form, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(ShowLanguage.Dubbed, (await db.Showtimes.AsNoTracking().SingleAsync()).Language);
    }

    [Fact]
    public async Task Cancelled_tickets_no_longer_lock_a_showtime()
    {
        var (db, _, room, showtime) = await SeedAsync();
        var booking = await SellAsync(db, showtime, room);
        await Cancellation(db).CancelAsync(booking.Id, "Hỏng máy", "admin", CancellationToken.None);
        var form = Form(showtime);
        form.StartTimeLocal = form.StartTimeLocal!.Value.AddHours(3);

        var result = await Showtimes(db).Edit(showtime.Id, form, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task Movie_with_upcoming_tickets_cannot_be_hidden()
    {
        var (db, movie, room, showtime) = await SeedAsync();
        await SellAsync(db, showtime, room);

        await new MoviesController(db, Clock()).Delete(movie.Id, CancellationToken.None);

        Assert.True((await db.Movies.AsNoTracking().SingleAsync(m => m.Id == movie.Id)).IsActive);
    }

    [Fact]
    public async Task Movie_form_cannot_bypass_the_hide_rule()
    {
        var (db, movie, room, showtime) = await SeedAsync();
        await SellAsync(db, showtime, room);
        var form = MovieFormModel.From(movie);
        form.IsActive = false;
        var controller = new MoviesController(db, Clock());

        var result = await controller.Edit(movie.Id, form, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.True((await db.Movies.AsNoTracking().SingleAsync(m => m.Id == movie.Id)).IsActive);
    }

    [Fact]
    public async Task Movie_can_be_hidden_once_its_showtimes_have_passed()
    {
        var (db, movie, room, showtime) = await SeedAsync();
        await SellAsync(db, showtime, room);
        var later = new FixedTimeProvider(StartUtc.AddDays(1));

        await new MoviesController(db, later).Delete(movie.Id, CancellationToken.None);

        Assert.False((await db.Movies.AsNoTracking().SingleAsync(m => m.Id == movie.Id)).IsActive);
    }

    [Fact]
    public async Task Same_title_and_release_date_is_a_duplicate_movie()
    {
        var (db, _, _, _) = await SeedAsync();
        var controller = new MoviesController(db, Clock());
        var form = new MovieFormModel { Title = "phim a", ReleaseDate = new DateOnly(2026, 1, 1) };

        var result = await controller.Create(form, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Theater_with_upcoming_tickets_cannot_be_closed()
    {
        var (db, _, room, showtime) = await SeedAsync();
        await SellAsync(db, showtime, room);

        await new TheatersController(db, Clock()).Delete(room.TheaterId, CancellationToken.None);

        Assert.True((await db.Theaters.AsNoTracking().SingleAsync()).IsActive);
    }

    [Theory]
    [InlineData("hà nội")]
    [InlineData("HA NOI")]
    public async Task New_branch_reuses_the_existing_city_spelling(string typed)
    {
        var (db, _, _, _) = await SeedAsync();
        var form = new TheaterFormModel { Name = "CGV Khác", City = typed, Address = "2 Test" };

        await new TheatersController(db, Clock()).Create(form, CancellationToken.None);

        Assert.Equal("Hà Nội", (await db.Theaters.AsNoTracking().SingleAsync(t => t.Name == "CGV Khác")).City);
    }

    [Fact]
    public async Task Cinema_cancellation_frees_seats_and_asks_for_a_refund()
    {
        var (db, _, room, showtime) = await SeedAsync();
        var booking = await SellAsync(db, showtime, room);

        var outcome = await Cancellation(db).CancelAsync(booking.Id, "Phòng gặp sự cố", "admin", CancellationToken.None);

        Assert.True(outcome.Succeeded);
        var stored = await db.Bookings.AsNoTracking().Include(b => b.Payment).SingleAsync();
        Assert.Equal(BookingStatus.Cancelled, stored.Status);
        Assert.Equal(PaymentStatus.RefundPending, stored.Payment!.Status);
        Assert.Empty(await db.BookingSeats.ToListAsync());
        var mail = await db.OutboxEmails.SingleAsync();
        Assert.Equal("khach@example.com", mail.ToAddress);
        Assert.Contains("Phòng gặp sự cố", mail.Body);
    }

    [Fact]
    public async Task Cancellation_needs_a_reason()
    {
        var (db, _, room, showtime) = await SeedAsync();
        var booking = await SellAsync(db, showtime, room);

        var outcome = await Cancellation(db).CancelAsync(booking.Id, "   ", "admin", CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(BookingStatus.Confirmed, (await db.Bookings.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Checked_in_ticket_cannot_be_cancelled()
    {
        var (db, _, room, showtime) = await SeedAsync();
        var booking = await SellAsync(db, showtime, room);
        booking.CheckedInUtc = NowUtc;
        await db.SaveChangesAsync();

        var outcome = await Cancellation(db).CancelAsync(booking.Id, "Lý do", "admin", CancellationToken.None);

        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public async Task Ticket_of_a_started_showtime_cannot_be_cancelled()
    {
        var (db, _, room, showtime) = await SeedAsync();
        var booking = await SellAsync(db, showtime, room);
        var late = new BookingCancellationService(db, new FixedTimeProvider(StartUtc.AddMinutes(1)), new DisplayTimeZone());

        var outcome = await late.CancelAsync(booking.Id, "Lý do", "admin", CancellationToken.None);

        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public async Task Cancelling_a_showtime_cancels_every_ticket_and_hides_it()
    {
        var (db, _, room, showtime) = await SeedAsync();
        await SellAsync(db, showtime, room);
        await SellAsync(db, showtime, room);

        var outcome = await Cancellation(db).CancelShowtimeAsync(showtime.Id, "Hỏng máy", "admin", CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.All(await db.Bookings.AsNoTracking().ToListAsync(), b => Assert.Equal(BookingStatus.Cancelled, b.Status));
        Assert.False((await db.Showtimes.AsNoTracking().SingleAsync()).IsActive);
        Assert.Equal(2, await db.OutboxEmails.CountAsync());
    }

    [Fact]
    public async Task Refund_can_only_be_marked_on_a_pending_refund()
    {
        var (db, _, room, showtime) = await SeedAsync();
        var booking = await SellAsync(db, showtime, room);
        var service = Cancellation(db);

        Assert.False((await service.MarkRefundedAsync(booking.Id, CancellationToken.None)).Succeeded);

        await service.CancelAsync(booking.Id, "Lý do", "admin", CancellationToken.None);
        Assert.True((await service.MarkRefundedAsync(booking.Id, CancellationToken.None)).Succeeded);
        Assert.Equal(PaymentStatus.Refunded, (await db.Payments.AsNoTracking().SingleAsync()).Status);
    }
}
