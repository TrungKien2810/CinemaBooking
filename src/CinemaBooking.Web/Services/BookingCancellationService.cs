using System.Security.Cryptography;
using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Services;

/// <summary>
/// Hủy vé + hoàn tiền. Trạng thái được đổi theo từng bước để tránh hoàn tiền hai lần:
/// <c>Succeeded → RefundPending → Refunded</c> (hoặc <c>RefundFailed</c>).
/// <para>
/// Refund là cuộc gọi HTTP ra ngoài, KHÔNG đặt trong transaction DB. Nếu gọi VNPAY lỗi thì đơn
/// vẫn giữ nguyên <c>Confirmed</c> và ghế chưa bị trả — khách có thể bấm hủy lại, tránh cảnh
/// “mất ghế nhưng chưa nhận tiền”.
/// </para>
/// </summary>
public sealed class BookingCancellationService : IBookingCancellationService
{
    private readonly CinemaDbContext _db;
    private readonly IVnpayRefundService _refund;
    private readonly VnpayOptions _vnpay;
    private readonly BookingCancellationOptions _policy;
    private readonly DisplayTimeZone _tz;
    private readonly TimeProvider _clock;
    private readonly ILogger<BookingCancellationService> _log;

    public BookingCancellationService(
        CinemaDbContext db,
        IVnpayRefundService refund,
        IOptions<VnpayOptions> vnpay,
        IOptions<BookingCancellationOptions> policy,
        DisplayTimeZone tz,
        TimeProvider clock,
        ILogger<BookingCancellationService> log)
    {
        _db = db;
        _refund = refund;
        _vnpay = vnpay.Value;
        _policy = policy.Value;
        _tz = tz;
        _clock = clock;
        _log = log;
    }

    public Task<CancelEligibility> GetEligibilityAsync(Booking booking, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var start = booking.Showtime?.StartTimeUtc ?? DateTime.MaxValue;
        var paid = booking.Payment is
        {
            Status: PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.RefundFailed
        };
        var refund = paid ? booking.TotalAmount : 0m;

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Expired)
            return Task.FromResult(new CancelEligibility(false, "Đơn đã hủy hoặc hết hạn.", 0m));

        if (booking.Status == BookingStatus.Pending)
            return Task.FromResult(new CancelEligibility(true, null, 0m));

        if (start <= now)
            return Task.FromResult(new CancelEligibility(false, "Suất chiếu đã bắt đầu.", 0m));

        var hoursLeft = (start - now).TotalHours;
        if (hoursLeft < _policy.MinHoursBeforeShowtime)
        {
            return Task.FromResult(new CancelEligibility(false,
                $"Chỉ được hủy trước giờ chiếu {_policy.MinHoursBeforeShowtime} giờ.", 0m));
        }

        return Task.FromResult(new CancelEligibility(true, null, refund));
    }

    public async Task<CancelBookingResult> CancelAsync(CancelBookingRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.BookingCode))
            return new CancelBookingResult(CancelBookingOutcome.NotFound, "Thiếu mã vé.");

        var code = request.BookingCode.Trim().ToUpperInvariant();

        var booking = await _db.Bookings
            .Include(b => b.Showtime!).ThenInclude(s => s.Movie)
            .Include(b => b.Showtime!).ThenInclude(s => s.Room!).ThenInclude(r => r.Theater)
            .Include(b => b.Seats).ThenInclude(bs => bs.Seat)
            .Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.BookingCode == code, ct);

        if (booking is null)
            return new CancelBookingResult(CancelBookingOutcome.NotFound, "Không tìm thấy vé với mã này.");

        var auth = Authorize(booking, request);
        if (auth is not null)
            return new CancelBookingResult(CancelBookingOutcome.NotOwner, auth);

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Expired)
            return new CancelBookingResult(CancelBookingOutcome.AlreadyCancelled, "Đơn này đã hủy hoặc hết hạn.");

        var now = _clock.GetUtcNow().UtcDateTime;
        var start = booking.Showtime!.StartTimeUtc;

        // Admin được hủy sát giờ hơn khách; khách phải tuân thủ hạn hủy.
        if (!request.IsAdmin)
        {
            if (start <= now)
                return new CancelBookingResult(CancelBookingOutcome.TooLate, "Suất chiếu đã bắt đầu, không thể hủy.");

            if ((start - now).TotalHours < _policy.MinHoursBeforeShowtime)
            {
                return new CancelBookingResult(CancelBookingOutcome.TooLate,
                    $"Chỉ được hủy trước giờ chiếu {_policy.MinHoursBeforeShowtime} giờ.");
            }
        }
        var payment = booking.Payment;
        var refundAmount = payment is
        {
            Status: PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.RefundFailed
        }
            ? booking.TotalAmount
            : 0m;
        string? refundTxnNo = null;
        string? refundRequestId = null;
        string? refundCode = null;

        // ── Bước 1: hoàn tiền (nếu đã thu tiền) ────────────────────────────────
        if (refundAmount > 0 && payment is not null)
        {
            if (_vnpay.RefundEnabled)
            {
                var started = await TryStartRefundAsync(payment, ct);
                if (started is not null)
                    return new CancelBookingResult(CancelBookingOutcome.NotCancellable, started);

                if (string.IsNullOrWhiteSpace(payment.VnpayCreateDate))
                {
                    await MarkRefundFailedAsync(payment, "Thiếu VnpayCreateDate của giao dịch gốc.", ct: ct);
                    return new CancelBookingResult(CancelBookingOutcome.RefundFailed,
                        "Không hoàn tiền được: thiếu ngày giao dịch gốc. Vui lòng liên hệ rạp.");
                }

                refundRequestId = NewRequestId();
                var result = await _refund.RefundAsync(new VnpayRefundRequest(
                    RequestId: refundRequestId,
                    TxnRef: booking.BookingCode,
                    Amount: refundAmount,
                    TransactionNo: payment.TransactionId,
                    TransactionDate: payment.VnpayCreateDate,
                    OrderInfo: $"Hoan tien ve {booking.BookingCode}",
                    CreateBy: _vnpay.CreateBy,
                    IpAddr: _vnpay.IpAddr,
                    FullRefund: true), ct);

                if (!result.Success)
                {
                    await MarkRefundFailedAsync(payment, result.Message ?? "VNPAY từ chối hoàn tiền.", result, ct);
                    _log.LogWarning("Hoàn tiền thất bại cho {Code}: {Message}", booking.BookingCode, result.Message);
                    return new CancelBookingResult(CancelBookingOutcome.RefundFailed,
                        $"Chưa hoàn được tiền: {result.Message ?? "VNPAY từ chối"}. Vé vẫn giữ nguyên, bạn có thể thử lại.");
                }

                refundTxnNo = result.RefundTransactionNo;
                refundCode = result.ResponseCode;
            }
            else
            {
                // Hoàn tiền mô phỏng: không gọi VNPAY, chỉ ghi nhận để demo luồng.
                refundCode = "MOCK";
            }
        }

        // ── Bước 2: trả ghế + đóng đơn trong một transaction ──────────────────
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var seatIds = booking.Seats.Select(s => s.SeatId).ToList();
        _db.BookingSeats.RemoveRange(booking.Seats);

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledUtc = now;
        booking.CancelledBy = request.Actor;
        booking.CancellationReason = request.IsAdmin ? "Admin hủy" : "Khách tự hủy";

        if (payment is not null)
        {
            if (refundAmount > 0)
            {
                payment.Status = PaymentStatus.Refunded;
                payment.RefundedAmount = refundAmount;
                payment.RefundedUtc = now;
                payment.RefundTransactionNo = refundTxnNo;
                payment.RefundRequestId = refundRequestId;
                payment.RefundResponseCode = refundCode;
                payment.RefundMessage = _vnpay.RefundEnabled ? "VNPAY refund" : "Hoàn tiền mô phỏng";
            }
            else if (payment.Status == PaymentStatus.Pending)
            {
                payment.Status = PaymentStatus.Failed;
            }
        }

        _db.OutboxEmails.Add(BuildCancellationEmail(booking, refundAmount));

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new CancelBookingResult(
            CancelBookingOutcome.Success,
            refundAmount > 0
                ? $"Đã hủy vé và hoàn {refundAmount:N0} ₫."
                : "Đã hủy vé.",
            refundAmount,
            booking.ShowtimeId,
            seatIds);
    }

    /// <summary>
    /// Chuyển trạng thái thanh toán sang RefundPending một cách nguyên tử. Nếu request khác đã
    /// chuyển trước thì trả về thông báo thay vì gọi VNPAY lần hai.
    /// </summary>
    private async Task<string?> TryStartRefundAsync(Payment payment, CancellationToken ct)
    {
        if (payment.Status == PaymentStatus.RefundPending)
            return "Đơn đang được hoàn tiền, vui lòng chờ.";

        var affected = await _db.Payments
            .Where(p => p.Id == payment.Id
                        && (p.Status == PaymentStatus.Succeeded || p.Status == PaymentStatus.RefundFailed))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PaymentStatus.RefundPending)
                .SetProperty(p => p.RefundMessage, "Đang gửi yêu cầu hoàn tiền."), ct);

        if (affected == 0)
            return "Đơn không còn ở trạng thái có thể hoàn tiền.";

        payment.Status = PaymentStatus.RefundPending;
        return null;
    }

    private async Task MarkRefundFailedAsync(
        Payment payment, string message, VnpayRefundResult? result = null, CancellationToken ct = default)
    {
        var trimmed = message.Length > 500 ? message[..500] : message;
        var code = result?.ResponseCode;

        // TryStartRefundAsync đã chuyển trạng thái bằng ExecuteUpdate (bỏ qua change tracker).
        // Nếu thất bại lần hai, tracker vẫn coi Status gốc là RefundFailed nên SaveChanges sẽ
        // không ghi đè RefundPending trong DB. Ghi thẳng xuống DB để trạng thái luôn cho phép thử lại.
        await _db.Payments
            .Where(p => p.Id == payment.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PaymentStatus.RefundFailed)
                .SetProperty(p => p.RefundMessage, trimmed)
                .SetProperty(p => p.RefundResponseCode, code), ct);

        payment.Status = PaymentStatus.RefundFailed;
        payment.RefundMessage = trimmed;
        payment.RefundResponseCode = code;
    }

    private string? Authorize(Booking booking, CancelBookingRequest request)
    {
        if (request.IsAdmin) return null;

        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            if (string.Equals(booking.UserId, request.UserId, StringComparison.Ordinal))
                return null;

            // Vé guest (chưa gắn tài khoản) nhưng email đặt vé trùng email tài khoản đang đăng nhập.
            if (booking.UserId is null
                && !string.IsNullOrWhiteSpace(request.Email)
                && string.Equals(booking.CustomerEmail, request.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return "Đây không phải vé của tài khoản bạn.";
        }

        // Guest checkout: chỉ cho hủy khi đơn chưa gắn tài khoản và email khớp.
        if (_policy.AllowGuestCancel
            && booking.UserId is null
            && !string.IsNullOrWhiteSpace(request.Email)
            && string.Equals(booking.CustomerEmail, request.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return "Vui lòng đăng nhập đúng tài khoản đã đặt vé, hoặc nhập đúng email đặt vé.";
    }

    private OutboxEmail BuildCancellationEmail(Booking booking, decimal refundAmount)
    {
        var start = _tz.ToLocalTime(booking.Showtime!.StartTimeUtc);
        var seats = string.Join(", ", booking.Seats
            .Where(s => s.Seat is not null)
            .OrderBy(s => s.Seat!.RowNumber).ThenBy(s => s.Seat!.ColumnNumber)
            .Select(s => s.Seat!.Label));

        var refundLine = refundAmount > 0
            ? $"Số tiền hoàn: {refundAmount:N0} VND (về phương thức thanh toán ban đầu).\n"
            : "Đơn chưa thanh toán nên không phát sinh hoàn tiền.\n";

        var body =
            $"Xin chào {booking.CustomerName},\n\n" +
            $"Đơn {booking.BookingCode} đã được hủy.\n" +
            $"Phim: {booking.Showtime.Movie?.Title}\n" +
            $"Suất chiếu: {start:HH:mm dd/MM/yyyy}\n" +
            $"Ghế: {seats}\n" +
            refundLine +
            "\nCảm ơn bạn đã sử dụng CinemaBooking.";

        return new OutboxEmail
        {
            ToAddress = booking.CustomerEmail,
            Subject = $"Đã hủy vé {booking.BookingCode}",
            Body = body,
            Status = OutboxStatus.Pending,
            CreatedUtc = _clock.GetUtcNow().UtcDateTime,
        };
    }

    // VNPAY yêu cầu vnp_RequestId là alphanumeric 1..32 ký tự; không dùng dấu '-'.
    private string NewRequestId() =>
        $"RF{_clock.GetUtcNow().UtcDateTime:yyyyMMddHHmmss}{RandomNumberGenerator.GetInt32(0x10000000, 0x7FFFFFFF):X8}";
}
