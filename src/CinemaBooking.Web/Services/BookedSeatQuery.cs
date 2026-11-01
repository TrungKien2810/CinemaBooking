using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Ghế "đã bị chiếm" = có dòng <c>BookingSeats</c> của đơn <b>Confirmed</b>, hoặc của đơn <b>Pending</b>
/// vẫn còn trong hạn chờ thanh toán VNPAY (<see cref="VnpayOptions.PendingHoldWindow"/>).
/// <para>
/// KHÔNG được tính mọi dòng <c>BookingSeats</c>: đơn Pending bị bỏ dở (khách đóng tab ở cổng thanh
/// toán, hoặc giao dịch lỗi mà VNPAY không gọi về ReturnUrl/IPN) sẽ giữ ghế vĩnh viễn — ghế "mất"
/// dù chưa ai trả tiền. Quá hạn chờ thì ghế phải hiện lại là trống để khách khác đặt.
/// </para>
/// </summary>
public sealed class BookedSeatQuery : IBookedSeatQuery
{
    private readonly CinemaDbContext _db;
    private readonly TimeProvider _clock;
    private readonly VnpayOptions _vnpay;

    public BookedSeatQuery(CinemaDbContext db, TimeProvider clock, IOptions<VnpayOptions> vnpay)
    {
        _db = db;
        _clock = clock;
        _vnpay = vnpay.Value;
    }

    public async Task<IReadOnlySet<int>> GetBookedSeatIdsAsync(int showtimeId, CancellationToken ct = default)
    {
        var pendingCutoff = _clock.GetUtcNow().UtcDateTime - _vnpay.PendingHoldWindow;

        var seatIds = await _db.BookingSeats.AsNoTracking()
            .Where(bs => bs.ShowtimeId == showtimeId
                && (bs.Booking!.Status == BookingStatus.Confirmed
                    || (bs.Booking.Status == BookingStatus.Pending && bs.Booking.BookingTimeUtc > pendingCutoff)))
            .Select(bs => bs.SeatId)
            .ToListAsync(ct);

        return seatIds.ToHashSet();
    }
}
