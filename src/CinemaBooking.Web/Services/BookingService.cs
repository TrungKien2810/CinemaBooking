using CinemaBooking.Domain;
using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly ISeatLockManager _locks;
    private readonly ISeatPricingService _pricing;
    private readonly IVnpayPaymentService _vnpay;
    private readonly IBookingCodeGenerator _codes;
    private readonly TimeProvider _clock;
    private readonly IOptions<VnpayOptions> _vnpayOptions;
    private readonly DisplayTimeZone _tz;
    private readonly ILogger<BookingService> _log;
    private readonly IVoucherService? _vouchers;

    public BookingService(
        CinemaDbContext db,
        IBookedSeatQuery bookedSeats,
        ISeatLockManager locks,
        ISeatPricingService pricing,
        IVnpayPaymentService vnpay,
        IBookingCodeGenerator codes,
        TimeProvider clock,
        IOptions<VnpayOptions> vnpayOptions,
        DisplayTimeZone tz,
        ILogger<BookingService> log,
        IVoucherService? vouchers = null)
    {
        _vouchers = vouchers;
        _db = db;
        _bookedSeats = bookedSeats;
        _locks = locks;
        _pricing = pricing;
        _vnpay = vnpay;
        _codes = codes;
        _clock = clock;
        _vnpayOptions = vnpayOptions;
        _tz = tz;
        _log = log;
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

        // Chốt chặn cuối cho ghế đôi: client có thể gửi lên nửa cặp, nhưng không được bán nửa cặp.
        var pairingError = SeatPairing.Validate(showtime.Room.Seats, seatIds);
        if (pairingError is not null)
            return BookingResult.Failed(BookingResultKind.SeatsUnavailable, pairingError);

        // Đơn Pending bỏ dở (khách đóng tab ở cổng VNPAY / thanh toán lỗi mà cổng không gọi về) đã
        // quá hạn chờ vẫn còn dòng BookingSeats — unique index sẽ chặn người khác đặt lại ghế. Dọn
        // các đơn đó trước khi kiểm ghế để ghế chưa thanh toán được trả về cho người mua sau.
        await ExpireStalePendingBookingsAsync(showtime.Id, ct);

        // Kiểm tra sớm cho thông báo thân thiện; chốt chặn cuối vẫn là unique index (ShowtimeId, SeatId).
        var booked = await _bookedSeats.GetBookedSeatIdsAsync(showtime.Id, ct);
        if (seatIds.Any(booked.Contains))
            return BookingResult.Failed(BookingResultKind.SeatsUnavailable, "Có ghế vừa được người khác đặt. Vui lòng chọn lại.");

        // Khách có thể ngồi ở trang thanh toán lâu hơn thời gian giữ ghế: lock RAM đã hết hạn trong
        // lúc điền form. Ghi Booking lúc này là bán ghế mà không ai còn giữ — phải chọn lại.
        if (string.IsNullOrWhiteSpace(request.ConnectionId))
            return BookingResult.Failed(BookingResultKind.LocksExpired, "Mất kết nối giữ ghế, vui lòng tải lại trang.");

        var keys = seatIds.Select(seatId => new SeatKey(showtime.Id, seatId)).ToList();
        if (!_locks.ValidateLocks(keys, request.ConnectionId))
            return BookingResult.Failed(BookingResultKind.LocksExpired, "Thời gian giữ ghế đã hết hoặc ghế không còn do bạn giữ. Vui lòng chọn lại.");

        var startLocal = _tz.ToLocalTime(showtime.StartTimeUtc);
        var promotions = await PromotionRules.LoadForShowtimeAsync(
            _db, showtime.Room!.TheaterId, DateOnly.FromDateTime(startLocal), ct);
        var surcharges = await PricingLoader.LoadSurchargesAsync(_db, showtime.Room.TheaterId, ct);
        var booking = BuildBooking(request, showtime, seats, startLocal, promotions, surcharges);

        // Mã giảm giá kiểm lại Ở SERVER với tổng tiền thật của đơn (khách có thể sửa session/form), rồi chia vào giá từng ghế.
        if (!string.IsNullOrWhiteSpace(request.VoucherCode))
        {
            if (_vouchers is null)
                return BookingResult.Failed(BookingResultKind.VoucherInvalid, "Hệ thống chưa hỗ trợ mã giảm giá.");

            var check = await _vouchers.CheckAsync(
                request.VoucherCode, showtime.Room.TheaterId, booking.TotalAmount, request.UserId, request.CustomerEmail, ct);
            if (!check.IsOk) return BookingResult.Failed(BookingResultKind.VoucherInvalid, check.Message);

            ApplyVoucher(booking, check);
        }

        // Transaction 1: giữ chỗ. Suất chiếu đã bắt đầu hay chưa được kiểm lại ở trên; đơn Pending
        // + unique index chính là thứ giữ ghế trong lúc khách thanh toán (General.md §7.5) — mất
        // kết nối SignalR ở bước này cũng không làm mất ghế.
        await using (var transaction = await _db.Database.BeginTransactionAsync(ct))
        {
            _db.Bookings.Add(booking);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsDuplicateSeat(ex))
            {
                // Ai đó thắng cuộc đua giữa lúc kiểm ghế và lúc ghi: unique index (ShowtimeId, SeatId) từ chối.
                // Đây là chốt chặn cuối (General.md §5.1 mục 2) — trả lỗi thân thiện, không để lộ 500.
                _db.ChangeTracker.Clear();
                return BookingResult.Failed(BookingResultKind.SeatsUnavailable, "Có ghế vừa bị người khác đặt. Vui lòng chọn lại.");
            }

            await transaction.CommitAsync(ct);
        }

        var vnpay = await _vnpay.CreateVnpayPaymentAsync(new VnpayPaymentRequest(
            TxnRef: booking.BookingCode,
            Amount: booking.TotalAmount,
            OrderInfo: $"Thanh toan don hang {booking.BookingCode}",
            ReturnUrl: _vnpayOptions.Value.ReturnUrl,
            TmnCode: _vnpayOptions.Value.TmnCode,
            HashSecret: _vnpayOptions.Value.HashSecret,
            IpAddr: _vnpayOptions.Value.IpAddr,
            Locale: _vnpayOptions.Value.Locale,
            OrderType: _vnpayOptions.Value.OrderType,
            ExpireMinutes: _vnpayOptions.Value.ExpireMinutes), ct);

        if (!vnpay.Success)
        {
            // Máy dev/demo chưa có Vnpay:HashSecret: mô phỏng thanh toán thành công để luồng đặt vé
            // chạy trọn vẹn. Cờ này mặc định TẮT trong code, chỉ bật ở appsettings demo — bật ở
            // production nghĩa là bán vé miễn phí.
            if (_vnpayOptions.Value.SimulateWhenUnconfigured
                && string.IsNullOrWhiteSpace(_vnpayOptions.Value.HashSecret))
            {
                _log.LogWarning(
                    "VNPAY chưa cấu hình (Vnpay:HashSecret trống) — MÔ PHỎNG thanh toán cho đơn {BookingCode}. " +
                    "Đặt Vnpay:SimulateWhenUnconfigured=false khi chạy thật.",
                    booking.BookingCode);

                // Trả lock RAM như nhánh chuyển hướng VNPAY (DB đã giữ ghế bằng BookingSeats).
                foreach (var key in keys) _locks.Release(key, request.ConnectionId);

                // Đi qua đúng ConfirmAsync của luồng thật (ReturnUrl là request khác, DbContext khác),
                // nên xóa change tracker để đọc lại đơn từ DB thay vì dùng entity đang track.
                _db.ChangeTracker.Clear();
                return await ConfirmAsync(booking.BookingCode, new VnpayVerifyResult(
                    SignatureValid: true,
                    PaymentSucceeded: true,
                    TxnRef: booking.BookingCode,
                    ResponseCode: "00",
                    TransactionId: $"SIM-{booking.BookingCode}",
                    BankCode: "SIMULATED",
                    Amount: booking.TotalAmount,
                    FailureReason: null), ct);
            }

            // Không tạo được yêu cầu thanh toán: xóa BookingSeats để trả ghế, giữ lại Booking làm
            // lịch sử. Lock RAM của khách GIỮ NGUYÊN để họ bấm "Thanh toán" lại được ngay — ghế vẫn
            // là của họ trong thời gian giữ chỗ.
            _db.BookingSeats.RemoveRange(booking.Seats);
            booking.Status = BookingStatus.Cancelled;
            booking.Payment!.Status = PaymentStatus.Failed;
            await _db.SaveChangesAsync(ct);

            return BookingResult.Failed(BookingResultKind.PaymentFailed,
                vnpay.FailureReason ?? "Không tạo được yêu cầu thanh toán. Vui lòng thử lại.");
        }

        // VNPAY: khách sẽ được chuyển hướng sang cổng thanh toán. Booking + Payment đang ở trạng
        // thái Pending nên BookingSeats vẫn giữ ghế (unique index (ShowtimeId, SeatId)); RAM lock có
        // thể nhả ở đây vì DB đã giữ thay. Kết quả xác nhận (thành công/hủy) do VNPAY trả về
        // ReturnUrl/IPN rồi gọi <see cref="ConfirmAsync"/>.
        booking.Payment!.VnpayCreateDate = vnpay.CreateDate;
        await _db.SaveChangesAsync(ct);

        foreach (var key in keys) _locks.Release(key, request.ConnectionId);

        return BookingResult.RedirectToPayment(booking.BookingCode, vnpay.PaymentUrl ?? string.Empty);
    }

    /// <summary>
    /// Xác nhận kết quả thanh toán VNPAY cho một đơn (gọi từ ReturnUrl sau khi khách thanh toán xong
    /// ở cổng VNPAY, hoặc từ IPN notify). Giao dịch thành công → sang <see cref="BookingStatus.Confirmed"/>
    /// + gửi email xác nhận vào outbox; không thành / số tiền không khớp → trả ghế và hủy đơn.
    /// </summary>
    public async Task<BookingResult> ConfirmAsync(string bookingCode, VnpayVerifyResult verify, CancellationToken ct = default)
    {
        var booking = await _db.Bookings
            .Include(b => b.Payment)
            .Include(b => b.Showtime!).ThenInclude(sh => sh.Movie)
            .Include(b => b.Seats).ThenInclude(bs => bs.Seat)
            .FirstOrDefaultAsync(b => b.BookingCode == bookingCode, ct);

        if (booking is null)
            return BookingResult.Failed(BookingResultKind.PaymentFailed, "Không tìm thấy đơn đặt vé để xác nhận.");

        // Chữ ký sai thì KHÔNG được đụng vào đơn: nếu không, chỉ cần gọi ReturnUrl với tham số rác là
        // hủy được đơn (và ghế) của người khác. Chỉ VNPAY ký đúng mới được đổi trạng thái.
        if (!verify.SignatureValid)
            return BookingResult.Failed(BookingResultKind.PaymentFailed, "Chữ ký VNPAY không hợp lệ — đơn được giữ nguyên.");

        if (!string.Equals(verify.TxnRef, booking.BookingCode, StringComparison.OrdinalIgnoreCase))
            return BookingResult.Failed(BookingResultKind.PaymentFailed, "Mã giao dịch không khớp với đơn đặt vé.");

        // Đơn không còn Pending thì coi như đã xử lý — không thao tác lại (tránh xác nhận trùng).
        if (booking.Status != BookingStatus.Pending)
            return BookingResult.Failed(BookingResultKind.PaymentFailed, "Đơn này đã được xử lý trước đó.");

        var amountMatches = Math.Round(booking.TotalAmount, 2) == Math.Round(verify.Amount, 2);

        if (!verify.PaymentSucceeded || !amountMatches)
        {
            // Thanh toán không thành / số tiền không khớp: trả ghế để người khác đặt được.
            _db.BookingSeats.RemoveRange(booking.Seats);
            booking.Status = BookingStatus.Cancelled;
            booking.Payment!.Status = PaymentStatus.Failed;
            await _db.SaveChangesAsync(ct);

            return BookingResult.Failed(BookingResultKind.PaymentFailed, verify.FailureReason ?? "Thanh toán không thành công. Vui lòng thử lại.");
        }

        booking.Status = BookingStatus.Confirmed;
        booking.Payment!.Status = PaymentStatus.Succeeded;
        booking.Payment.TransactionId = verify.TransactionId;
        booking.Payment.PaymentMethod = "VNPAY";
        booking.Payment.VnpayPayDate = verify.PayDate;
        booking.Payment.PaidUtc = ParseVnpayDateToUtc(verify.PayDate) ?? _clock.GetUtcNow().UtcDateTime;

        var seats = booking.Seats.Where(bs => bs.Seat is not null).Select(bs => bs.Seat!).ToList();
        _db.OutboxEmails.Add(BuildConfirmationEmail(booking, booking.Showtime!, seats));

        await _db.SaveChangesAsync(ct);

        return BookingResult.Success(booking.BookingCode);
    }

    /// <summary>Gửi lại email xác nhận cho đơn đã Confirmed (không đổi trạng thái đơn).</summary>
    public async Task<BookingResult> ResendConfirmationAsync(string bookingCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bookingCode))
            return BookingResult.Failed(BookingResultKind.PaymentFailed, "Thiếu mã vé.");

        var code = bookingCode.Trim().ToUpperInvariant();
        var booking = await _db.Bookings
            .Include(b => b.Showtime!).ThenInclude(s => s.Movie)
            .Include(b => b.Seats).ThenInclude(bs => bs.Seat)
            .FirstOrDefaultAsync(b => b.BookingCode == code, ct);

        if (booking is null)
            return BookingResult.Failed(BookingResultKind.PaymentFailed, "Không tìm thấy vé.");

        if (booking.Status != BookingStatus.Confirmed)
            return BookingResult.Failed(BookingResultKind.PaymentFailed,
                "Chỉ gửi lại email cho vé đã xác nhận.");

        var seats = booking.Seats.Where(bs => bs.Seat is not null).Select(bs => bs.Seat!).ToList();
        _db.OutboxEmails.Add(BuildConfirmationEmail(booking, booking.Showtime!, seats));
        await _db.SaveChangesAsync(ct);

        return BookingResult.Success(booking.BookingCode);
    }

    /// <summary>
    /// Hết hạn các đơn <c>Pending</c> đã quá <see cref="VnpayOptions.PendingHoldWindow"/> của một suất
    /// chiếu và trả ghế (xóa dòng <c>BookingSeats</c> — unique index không lọc theo trạng thái đơn).
    /// <para>
    /// Không có bước này thì một lần thanh toán lỗi/bỏ dở sẽ khóa ghế vĩnh viễn: đơn không bao giờ
    /// được xác nhận nhưng dòng BookingSeats vẫn còn, nên ghế không ai đặt lại được.
    /// </para>
    /// </summary>
    private async Task ExpireStalePendingBookingsAsync(int showtimeId, CancellationToken ct)
    {
        var cutoff = _clock.GetUtcNow().UtcDateTime - _vnpayOptions.Value.PendingHoldWindow;

        var stale = await _db.Bookings
            .Include(b => b.Seats)
            .Include(b => b.Payment)
            .Where(b => b.ShowtimeId == showtimeId
                     && b.Status == BookingStatus.Pending
                     && b.BookingTimeUtc <= cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        foreach (var booking in stale)
        {
            _db.BookingSeats.RemoveRange(booking.Seats);
            booking.Status = BookingStatus.Expired;
            if (booking.Payment is not null) booking.Payment.Status = PaymentStatus.Failed;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Nhận diện vi phạm unique index (ShowtimeId, SeatId). SQL Server: "Cannot insert duplicate key
    /// ... 'BookingSeats'"; SQLite: "UNIQUE constraint failed: BookingSeats...". Dò theo thông điệp
    /// để chạy được trên cả hai provider, không phụ thuộc mã lỗi riêng của từng DB.
    /// </summary>
    private static bool IsDuplicateSeat(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("BookingSeats", StringComparison.OrdinalIgnoreCase)
            && (message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Trừ mã giảm giá khỏi đơn: chia theo tỷ lệ giá từng ghế để doanh thu (tính theo ghế) vẫn khớp tổng đơn.</summary>
    private static void ApplyVoucher(Booking booking, VoucherResult check)
    {
        var shares = VoucherRules.Allocate(check.Discount, booking.Seats.Select(s => s.PriceAtBooking).ToList());
        for (var i = 0; i < booking.Seats.Count; i++)
        {
            var seat = booking.Seats.ElementAt(i);
            seat.PriceAtBooking -= shares[i];
            seat.VoucherShare = shares[i];
        }

        booking.VoucherCode = check.Voucher!.Code;
        booking.VoucherDiscount = check.Discount;
        booking.TotalAmount -= check.Discount;
        if (booking.Payment is not null) booking.Payment.Amount = booking.TotalAmount;
    }

    private Booking BuildBooking(
        BookingRequest request, Showtime showtime, IReadOnlyList<Seat> seats,
        DateTime startLocal, IReadOnlyList<Promotion> promotions, SeatSurcharges? surcharges = null)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        // Giá chốt ở server từ cùng PromotionRules với sơ đồ ghế/thanh toán — không tin giá client.
        var priced = seats
            .Select(seat => (Seat: seat, Quote: PromotionRules.Quote(
                _pricing.PriceFor(showtime.BasePrice, seat.SeatType, surcharges), seat.SeatType, startLocal, promotions)))
            .ToList();
        var total = priced.Sum(x => x.Quote.FinalPrice);

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
                PriceAtBooking = x.Quote.FinalPrice,
                DiscountAmount = x.Quote.Discount,
                PromotionTitle = x.Quote.PromotionTitle,
            }).ToList(),
            Payment = new Payment
            {
                Amount = total,
                PaymentMethod = "VNPAY",
                Status = PaymentStatus.Pending,
                CreatedUtc = nowUtc,
            },
        };
    }

    /// <summary>
    /// Thư xác nhận chỉ là dữ liệu trong outbox; nội dung hiển thị ở đây phải đủ để khách đọc lại
    /// mà không cần mở DB (mã vé, phim, suất, ghế, số tiền).
    /// </summary>
    private OutboxEmail BuildConfirmationEmail(Booking booking, Showtime showtime, IReadOnlyList<Seat> seats)
    {
        var labels = string.Join(", ", seats
            .OrderBy(s => s.RowNumber).ThenBy(s => s.ColumnNumber)
            .Select(s => s.Label));

        // Giờ trong email phải là giờ rạp (GMT+7), không phải UTC trong DB — khách đọc email
        // không được thấy lệch 7 tiếng so với trang web.
        var startLocal = _tz.ToLocalTime(showtime.StartTimeUtc);

        var body =
            $"Xin chào {booking.CustomerName},\n\n" +
            $"Đơn {booking.BookingCode} của bạn đã được xác nhận.\n" +
            $"Phim: {showtime.Movie?.Title}\n" +
            $"Suất chiếu: {startLocal:HH:mm dd/MM/yyyy}\n" +
            $"Ghế: {labels}\n" +
            (booking.VoucherCode is null ? "" : $"Mã giảm giá {booking.VoucherCode}: -{booking.VoucherDiscount.Vnd()}\n") +
            $"Tổng tiền: {booking.TotalAmount.Vnd()}\n\n" +
            "Cảm ơn bạn đã đặt vé tại CinemaBooking.";

        return new OutboxEmail
        {
            ToAddress = booking.CustomerEmail,
            Subject = $"Xác nhận đặt vé {booking.BookingCode}",
            Body = body,
            Status = OutboxStatus.Pending,
            CreatedUtc = _clock.GetUtcNow().UtcDateTime,
        };
    }

    /// <summary>Đổi <c>vnp_PayDate</c> (GMT+7, yyyyMMddHHmmss) sang UTC để lưu DB.</summary>
    private static DateTime? ParseVnpayDateToUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParseExact(value, "yyyyMMddHHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var local)
            ? local.AddHours(-7)
            : null;
    }

    /// <summary>
    /// Cùng điều kiện "suất còn bán" với <c>BookingController.FindBookableShowtimeAsync</c>:
    /// suất chưa bắt đầu, phim và chi nhánh chưa ẩn.
    /// </summary>
    private Task<Showtime?> FindBookableShowtimeAsync(int showtimeId, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        return _db.Showtimes
            .Include(s => s.Movie)
            .Include(s => s.Room!).ThenInclude(r => r.Seats)
            .FirstOrDefaultAsync(s => s.Id == showtimeId
                && s.StartTimeUtc > nowUtc
                && s.IsActive
                && s.Movie!.IsActive
                && s.Room!.Theater!.IsActive, ct);
    }
}
