using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Điều phối đặt vé ở phía server. Service tự đọc lại ghế từ DB và giá từ
/// <see cref="ISeatPricingService"/> — sơ đồ ghế hiển thị giá và bước ghi Booking phải dùng CÙNG
/// một công thức, hai chỗ tự tính riêng thì sớm muộn sẽ lệch nhau.
/// <para>
/// Booking + BookingSeats + Payment được ghi trong MỘT transaction: lỗi ở bất kỳ bước nào thì
/// coi như chưa có gì được ghi, không để lại đơn nửa vời trong DB.
/// </para>
/// </summary>
public sealed class BookingService : IBookingService
{
    private readonly CinemaDbContext _db;
    private readonly IBookedSeatQuery _bookedSeats;
    private readonly ISeatPricingService _pricing;
    private readonly IBookingCodeGenerator _codes;
    private readonly TimeProvider _clock;

    public BookingService(
        CinemaDbContext db,
        IBookedSeatQuery bookedSeats,
        ISeatPricingService pricing,
        IBookingCodeGenerator codes,
        TimeProvider clock)
    {
        _db = db;
        _bookedSeats = bookedSeats;
        _pricing = pricing;
        _codes = codes;
        _clock = clock;
    }

    public async Task<BookingResult> PlaceAsync(BookingRequest request, CancellationToken ct = default)
    {
        var showtime = await FindBookableShowtimeAsync(request.ShowtimeId, ct);
        if (showtime is null)
            return BookingResult.Failed(BookingResultKind.ShowtimeNotBookable, "Suất chiếu không còn mở bán.");

        var seatIds = request.SeatIds.Distinct().ToList();
        if (seatIds.Count == 0)
            return BookingResult.Failed(BookingResultKind.SeatsUnavailable, "Chưa chọn ghế nào.");

        // Ghế phải thuộc đúng phòng của suất — id gửi lên có thể là bất kỳ.
        var seats = showtime.Room!.Seats.Where(s => seatIds.Contains(s.Id)).ToList();
        if (seats.Count != seatIds.Count)
            return BookingResult.Failed(BookingResultKind.SeatsUnavailable, "Có ghế không thuộc phòng chiếu của suất này.");

        // Kiểm tra sớm cho thông báo thân thiện; chốt chặn cuối vẫn là unique index (ShowtimeId, SeatId).
        var booked = await _bookedSeats.GetBookedSeatIdsAsync(showtime.Id, ct);
        if (seatIds.Any(booked.Contains))
            return BookingResult.Failed(BookingResultKind.SeatsUnavailable, "Có ghế vừa được người khác đặt. Vui lòng chọn lại.");

        var booking = BuildBooking(request, showtime, seats);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return BookingResult.Success(booking.BookingCode);
    }

    private Booking BuildBooking(BookingRequest request, Showtime showtime, IReadOnlyList<Seat> seats)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var priced = seats
            .Select(seat => (Seat: seat, Price: _pricing.PriceFor(showtime.BasePrice, seat.SeatType)))
            .ToList();
        var total = priced.Sum(x => x.Price);

        return new Booking
        {
            BookingCode = _codes.Next(),
            UserId = request.UserId,
            ShowtimeId = showtime.Id,
            CustomerName = request.CustomerName.Trim(),
            CustomerEmail = request.CustomerEmail.Trim(),
            CustomerPhone = string.IsNullOrWhiteSpace(request.CustomerPhone) ? null : request.CustomerPhone.Trim(),
            BookingTimeUtc = nowUtc,
            TotalAmount = total,
            Status = BookingStatus.Pending,
            Seats = priced.Select(x => new BookingSeat
            {
                SeatId = x.Seat.Id,
                ShowtimeId = showtime.Id,
                PriceAtBooking = x.Price,
            }).ToList(),
            Payment = new Payment
            {
                Amount = total,
                PaymentMethod = "Mock",
                Status = PaymentStatus.Pending,
                CreatedUtc = nowUtc,
            },
        };
    }

    /// <summary>
    /// Cùng điều kiện "suất còn bán" với <c>BookingController.FindBookableShowtimeAsync</c>:
    /// suất chưa bắt đầu, phim và chi nhánh chưa ẩn.
    /// </summary>
    private Task<Showtime?> FindBookableShowtimeAsync(int showtimeId, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        return _db.Showtimes
            .Include(s => s.Room!).ThenInclude(r => r.Seats)
            .FirstOrDefaultAsync(s => s.Id == showtimeId
                && s.StartTimeUtc > nowUtc
                && s.Movie!.IsActive
                && s.Room!.Theater!.IsActive, ct);
    }
}
