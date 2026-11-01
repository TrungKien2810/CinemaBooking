using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Logging;
using CinemaBooking.Worker.SeatRelease;
using CinemaBooking.Worker.Workers;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = Host.CreateApplicationBuilder(args);

// Cùng cấu hình log với Web: Development ra console một dòng, ngoài Development ra JSON.
builder.Logging.AddCinemaLogging(builder.Environment.IsDevelopment());

builder.Services.AddCinemaInfrastructure(builder.Configuration);

// Worker tổng hợp báo cáo theo ngày: dùng chung TimeProvider để test được bằng đồng hồ đứng yên.
builder.Services.TryAddSingleton(TimeProvider.System);

// ── Dev B: Worker phát "ghế trống" ra ngoài process (Sprint 4) ──
// Có URL của Web thì nói chuyện với SeatHub qua SignalR client; chưa cấu hình thì dùng bản chỉ log
// để Worker vẫn chạy được (ghế vẫn được nhả, chỉ chậm tin báo cho trình duyệt).
if (!string.IsNullOrWhiteSpace(builder.Configuration["WebHub:BaseUrl"]))
{
    builder.Services.Configure<WebHubOptions>(builder.Configuration.GetSection(WebHubOptions.SectionName));
    builder.Services.AddSingleton<ISeatReleaseNotifier, SignalRSeatReleaseNotifier>();
}
else
{
    builder.Services.AddSingleton<ISeatReleaseNotifier, LoggingSeatReleaseNotifier>();
}

// Trần retry / nhịp quét của hàng đợi email — đọc từ section EmailOutbox.
builder.Services.Configure<EmailOutboxOptions>(
    builder.Configuration.GetSection(EmailOutboxOptions.SectionName));

builder.Services.AddHostedService<ExpiredSeatCleaner>();
builder.Services.AddHostedService<ExpiredBookingCleaner>();
builder.Services.AddHostedService<EmailOutboxWorker>();
builder.Services.AddHostedService<DailyReportWorker>();

var host = builder.Build();
host.Run();
