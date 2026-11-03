using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Worker.Workers;

/// <summary>
/// Tổng hợp doanh thu theo ngày/chi nhánh/phim. Tự vá MỌI ngày còn thiếu báo cáo chứ không chỉ đúng
/// "hôm qua": Worker bị tắt vài hôm rồi bật lại vẫn dựng đủ số liệu, ngày không có doanh thu thì bỏ qua.
/// <para>
/// Ngày báo cáo là ngày theo giờ rạp (section <c>Display:TimeZoneOffsetHours</c>, mặc định UTC+7),
/// KHÔNG phải ngày UTC — trùng với cách trang Quản trị → Doanh thu gom nhóm. Lệch múi giờ thì đơn
/// đặt lúc 00:00–07:00 giờ Việt Nam sẽ rơi sang ngày hôm trước.
/// </para>
/// <para>
/// Mỗi dòng báo cáo gắn với một chi nhánh (<see cref="DailyRevenueReport.TheaterId"/> là FK bắt buộc):
/// giá vé và lịch chiếu đi theo chi nhánh nên doanh thu cũng phải chia theo đó (AD-4). Gom nhóm thiếu
/// TheaterId sẽ làm insert vi phạm khóa ngoại và worker không bao giờ ghi được gì.
/// </para>
/// </summary>
public sealed class DailyReportWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _displayOffset;
    private readonly ILogger<DailyReportWorker> _log;

    public DailyReportWorker(
        IServiceScopeFactory scopes,
        TimeProvider clock,
        IConfiguration config,
        ILogger<DailyReportWorker> log)
    {
        _scopes = scopes;
        _clock = clock;
        _log = log;

        // Cùng khóa cấu hình với Web (DisplayTimeZone.SectionName) để hai bên gom nhóm giống nhau.
        var offsetHours = config.GetValue("Display:TimeZoneOffsetHours", 7);
        _displayOffset = TimeSpan.FromHours(offsetHours);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await AggregateMissingDaysAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Lỗi khi tổng hợp báo cáo ngày — vòng lặp vẫn tiếp tục.");
            }
        }
    }

    /// <summary>internal để test gọi trực tiếp, không phải chờ đồng hồ 1 giờ của vòng lặp.</summary>
    internal async Task AggregateMissingDaysAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();

        var today = ToLocalDate(_clock.GetUtcNow().UtcDateTime);

        var bookingTimes = await db.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed)
            .Select(b => b.BookingTimeUtc)
            .ToListAsync(ct);

        var reported = (await db.DailyRevenueReports
            .Select(r => r.ReportDate).Distinct().ToListAsync(ct)).ToHashSet();

        var missing = bookingTimes
            .Select(ToLocalDate)
            .Where(day => day < today)
            .Distinct()
            .Where(day => !reported.Contains(day))
            .OrderBy(day => day);

        foreach (var day in missing)
        {
            await AggregateDayAsync(db, day, ct);
        }
    }

    private async Task AggregateDayAsync(CinemaDbContext db, DateOnly day, CancellationToken ct)
    {
        // Nửa khoảng [from, to) theo UTC ứng với trọn một ngày ở giờ rạp.
        var from = day.ToDateTime(TimeOnly.MinValue) - _displayOffset;
        var to = from.AddDays(1);

        // Lọc/nhóm bằng SQL, KHÔNG dùng AsParallel() trên IQueryable
        // (PLINQ chỉ chạy trên collection đã nằm trong bộ nhớ).
        var rows = await db.BookingSeats
            .Where(bs => bs.Booking!.Status == BookingStatus.Confirmed
                         && bs.Booking.BookingTimeUtc >= from
                         && bs.Booking.BookingTimeUtc < to)
            .GroupBy(bs => new { bs.Booking!.Showtime!.Room!.TheaterId, bs.Booking!.Showtime!.MovieId })
            .Select(g => new
            {
                g.Key.TheaterId,
                g.Key.MovieId,
                Tickets = g.Count(),
                Revenue = g.Sum(x => x.PriceAtBooking),
            })
            .ToListAsync(ct);

        if (rows.Count == 0) return;

        db.DailyRevenueReports.AddRange(rows.Select(r => new DailyRevenueReport
        {
            ReportDate = day,
            TheaterId = r.TheaterId,
            MovieId = r.MovieId,
            TicketsSold = r.Tickets,
            Revenue = r.Revenue,
            GeneratedUtc = _clock.GetUtcNow().UtcDateTime,
        }));

        await db.SaveChangesAsync(ct);
        _log.LogInformation("Đã tổng hợp báo cáo ngày {Day}: {Rows} dòng.", day, rows.Count);
    }

    private DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(utc + _displayOffset);
}
