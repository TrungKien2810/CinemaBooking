using CinemaBooking.Domain;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>Tra cứu đơn đặt vé: Admin thấy mọi chi nhánh, chủ chi nhánh chỉ thấy chi nhánh mình: lọc theo mã, chi nhánh, trạng thái; xem chi tiết.</summary>
public class BookingsController : AdminControllerBase
{
    /// <summary>Số đơn tối đa trên một trang danh sách — đơn mới nhất trước, lọc để thu hẹp.</summary>
    /// <summary>Số đơn mỗi trang của danh sách đơn đặt vé.</summary>
    public const int PageSize = 40;

    private readonly CinemaDbContext _db;
    private readonly DisplayTimeZone _tz;
    private readonly IBranchScope _branches;
    private readonly BookingCancellationService? _cancellation;
    private readonly TimeProvider _clock;

    public BookingsController(
        CinemaDbContext db, IOptions<DisplayTimeZone> tz, IBranchScope branches,
        BookingCancellationService? cancellation = null, TimeProvider? clock = null)
    {
        _db = db;
        _tz = tz.Value;
        _branches = branches;
        _cancellation = cancellation;
        _clock = clock ?? TimeProvider.System;
    }

    /// <param name="q">Tìm theo mã đơn, tên, email hoặc số điện thoại khách.</param>
    /// <param name="code">Tên tham số cũ của <paramref name="q"/> (link đã lưu vẫn chạy).</param>
    /// <param name="from">Đặt từ ngày (giờ rạp).</param>
    /// <param name="to">Đặt đến ngày (giờ rạp).</param>
    public async Task<IActionResult> Index(
        string? q, string? code, int? theaterId, BookingStatus? status, DateOnly? from, DateOnly? to, int? page, CancellationToken ct)
    {
        q = string.IsNullOrWhiteSpace(q) ? code : q;
        var scoped = await _branches.TheaterIdAsync(ct);
        if (scoped is not null)
        {
            if (theaterId is not null && theaterId != scoped) return Forbid();
            theaterId = scoped;
        }

        var query = _db.Bookings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var upper = term.ToUpperInvariant();
            // Tên khách tìm không dấu: so khớp trên danh sách tên riêng (ít hơn nhiều so với số đơn) rồi lọc lại ở SQL.
            var names = (await query.Select(b => b.CustomerName).Distinct().ToListAsync(ct))
                .Where(n => TextSearch.Matches(term, n))
                .ToList();
            query = query.Where(b => b.BookingCode.Contains(upper) || names.Contains(b.CustomerName)
                                     || b.CustomerEmail.Contains(term) || (b.CustomerPhone != null && b.CustomerPhone.Contains(term)));
        }
        if (from is not null && to is not null && from > to) (from, to) = (to, from);
        if (from is not null) query = query.Where(b => b.BookingTimeUtc >= _tz.DayRangeUtc(from.Value).FromUtc);
        if (to is not null) query = query.Where(b => b.BookingTimeUtc < _tz.DayRangeUtc(to.Value).ToUtc);
        if (theaterId is not null)
            query = query.Where(b => b.Showtime!.Room!.TheaterId == theaterId);
        if (status is not null)
            query = query.Where(b => b.Status == status);

        var total = await query.CountAsync(ct);
        var current = CinemaBooking.Web.Models.PageInfo.Clamp(page, total, PageSize);
        var rows = await query
            .OrderByDescending(b => b.BookingTimeUtc)
            .Skip((current - 1) * PageSize)
            .Take(PageSize)
            .Select(b => new
            {
                b.Id,
                b.BookingCode,
                b.CustomerName,
                b.Showtime!.Movie!.Title,
                TheaterName = b.Showtime.Room!.Theater!.Name,
                b.Showtime.StartTimeUtc,
                b.BookingTimeUtc,
                SeatCount = b.Seats.Count,
                b.TotalAmount,
                b.Status,
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new BookingAdminItem(
                r.Id, r.BookingCode, r.CustomerName, r.Title, r.TheaterName,
                _tz.ToLocalTime(r.StartTimeUtc), _tz.ToLocalTime(r.BookingTimeUtc),
                r.SeatCount, r.TotalAmount, r.Status))
            .ToList();

        var theatersQuery = _db.Theaters.AsNoTracking();
        if (scoped is not null) theatersQuery = theatersQuery.Where(t => t.Id == scoped);
        var theaters = await theatersQuery
            .OrderBy(t => t.City).ThenBy(t => t.Name)
            .Select(t => new SelectListItem(t.City + " · " + t.Name, t.Id.ToString()))
            .ToListAsync(ct);

        return View(new BookingsIndexViewModel(
            q?.Trim(), theaterId, status, theaters, items, PageSize, from, to, new CinemaBooking.Web.Models.PageInfo(current, PageSize, total)));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var b = await _db.Bookings.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.BookingCode,
                x.Status,
                x.BookingTimeUtc,
                x.CustomerName,
                x.CustomerEmail,
                x.CustomerPhone,
                x.Showtime!.Movie!.Title,
                TheaterName = x.Showtime.Room!.Theater!.Name,
                x.Showtime.Room.RoomName,
                x.Showtime.Room.TheaterId,
                x.Showtime.StartTimeUtc,
                x.TotalAmount,
                x.VoucherCode,
                x.VoucherDiscount,
                x.CheckedInUtc,
                PaymentStatus = (PaymentStatus?)x.Payment!.Status,
                x.Payment.TransactionId,
                Seats = x.Seats
                    .OrderBy(s => s.Seat!.RowNumber).ThenBy(s => s.Seat!.ColumnNumber)
                    .Select(s => new { s.Seat, s.PriceAtBooking })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (b is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(b.TheaterId, ct)) return Forbid();

        var seats = b.Seats
            .Select(s => new BookingSeatLine(s.Seat!.Label, s.Seat.SeatType, s.PriceAtBooking))
            .ToList();

        return View(new BookingDetailsViewModel(
            b.Id, b.BookingCode, b.Status, _tz.ToLocalTime(b.BookingTimeUtc),
            b.CustomerName, b.CustomerEmail, b.CustomerPhone,
            b.Title, b.TheaterName, b.RoomName, _tz.ToLocalTime(b.StartTimeUtc),
            b.TotalAmount, b.PaymentStatus, b.TransactionId, seats, b.VoucherCode, b.VoucherDiscount,
            CheckedInLocal: b.CheckedInUtc is { } at ? _tz.ToLocalTime(at) : null,
            CancelBlockedReason: BookingCancellationService.WhyNotCancellable(
                b.Status, b.CheckedInUtc, b.StartTimeUtc, _clock.GetUtcNow().UtcDateTime)));
    }

    /// <summary>Rạp hủy vé vì không chiếu được: Admin hoặc quản lý đúng chi nhánh. Khách không có lối vào này.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason, CancellationToken ct)
    {
        if (await TheaterOfAsync(id, ct) is not { } theaterId) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(theaterId, ct)) return Forbid();

        var outcome = await _cancellation!.CancelAsync(id, reason, User.Identity?.Name ?? "?", ct);
        Report(outcome);
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Đánh dấu rạp đã chuyển trả tiền cho khách.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRefunded(int id, CancellationToken ct)
    {
        if (await TheaterOfAsync(id, ct) is not { } theaterId) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(theaterId, ct)) return Forbid();

        Report(await _cancellation!.MarkRefundedAsync(id, ct));
        return RedirectToAction(nameof(Details), new { id });
    }

    private Task<int?> TheaterOfAsync(int bookingId, CancellationToken ct) =>
        _db.Bookings.AsNoTracking().Where(b => b.Id == bookingId)
            .Select(b => (int?)b.Showtime!.Room!.TheaterId)
            .FirstOrDefaultAsync(ct);

    private void Report(CancellationOutcome outcome)
    {
        if (outcome.Succeeded) Notify(outcome.Message);
        else NotifyError(outcome.Message);
    }
}
