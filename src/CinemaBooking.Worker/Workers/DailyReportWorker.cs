using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Worker.Workers;

/// <summary>Tổng hợp doanh thu theo ngày/phim.</summary>
public sealed class DailyReportWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DailyReportWorker> _log;

    public DailyReportWorker(IServiceScopeFactory scopes, ILogger<DailyReportWorker> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();

            var day = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
            var from = day.ToDateTime(TimeOnly.MinValue);
            var to = from.AddDays(1);

            if (await db.DailyRevenueReports.AnyAsync(r => r.ReportDate == day, ct)) continue;

            // Lọc/nhóm bằng SQL, KHÔNG dùng AsParallel() trên IQueryable
            // (PLINQ chỉ chạy trên collection đã nằm trong bộ nhớ).
            var rows = await db.BookingSeats
                .Where(bs => bs.Booking!.Status == BookingStatus.Confirmed
                             && bs.Booking.BookingTimeUtc >= from
                             && bs.Booking.BookingTimeUtc < to)
                .GroupBy(bs => bs.Booking!.Showtime!.MovieId)
                .Select(g => new { MovieId = g.Key, Tickets = g.Count(), Revenue = g.Sum(x => x.PriceAtBooking) })
                .ToListAsync(ct);

            db.DailyRevenueReports.AddRange(rows.Select(r => new DailyRevenueReport
            {
                ReportDate = day,
                MovieId = r.MovieId,
                TicketsSold = r.Tickets,
                Revenue = r.Revenue,
                GeneratedUtc = DateTime.UtcNow
            }));

            await db.SaveChangesAsync(ct);
            _log.LogInformation("Đã tổng hợp báo cáo ngày {Day}: {Rows} dòng.", day, rows.Count);
        }
    }
}
