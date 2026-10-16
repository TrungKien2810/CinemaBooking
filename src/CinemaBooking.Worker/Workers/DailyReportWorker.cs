using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Worker.Workers;

/// <summary>
/// Tổng hợp doanh thu theo ngày/phim. Tự vá MỌI ngày còn thiếu báo cáo chứ không chỉ đúng "hôm qua":
/// Worker bị tắt vài hôm rồi mở lại vẫn dựng đủ số liệu, ngày không có đơn thì bỏ qua.
/// </summary>
public sealed class DailyReportWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger<DailyReportWorker> _log;

    public DailyReportWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<DailyReportWorker> log)
    {
        _scopes = scopes;
        _clock = clock;
        _log = log;
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

    private async Task AggregateMissingDaysAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

        var bookingTimes = await db.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed)
            .Select(b => b.BookingTimeUtc)
            .ToListAsync(ct);

        var reported = (await db.DailyRevenueReports
            .Select(r => r.ReportDate).Distinct().ToListAsync(ct)).ToHashSet();

        var missing = bookingTimes
            .Select(DateOnly.FromDateTime)
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
        var from = day.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(1);

        // Lọc/nhóm bằng SQL, KHÔNG dùng AsParallel() trên IQueryable
        // (PLINQ chỉ chạy trên collection đã nằm trong bộ nhớ).
        var rows = await db.BookingSeats
            .Where(bs => bs.Booking!.Status == BookingStatus.Confirmed
                         && bs.Booking.BookingTimeUtc >= from
                         && bs.Booking.BookingTimeUtc < to)
            .GroupBy(bs => bs.Booking!.Showtime!.MovieId)
            .Select(g => new { MovieId = g.Key, Tickets = g.Count(), Revenue = g.Sum(x => x.PriceAtBooking) })
            .ToListAsync(ct);

        if (rows.Count == 0) return;

        db.DailyRevenueReports.AddRange(rows.Select(r => new DailyRevenueReport
        {
            ReportDate = day,
            MovieId = r.MovieId,
            TicketsSold = r.Tickets,
            Revenue = r.Revenue,
            GeneratedUtc = _clock.GetUtcNow().UtcDateTime,
        }));

        await db.SaveChangesAsync(ct);
        _log.LogInformation("Đã tổng hợp báo cáo ngày {Day}: {Rows} dòng.", day, rows.Count);
    }
}
