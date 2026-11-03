using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Logging;
using CinemaBooking.Worker.Workers;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = Host.CreateApplicationBuilder(args);

// Cùng cấu hình log với Web: Development ra console một dòng, ngoài Development ra JSON.
builder.Logging.AddCinemaLogging(builder.Environment.IsDevelopment());

builder.Services.AddCinemaInfrastructure(builder.Configuration);

// Worker tổng hợp báo cáo theo ngày: dùng chung TimeProvider để test được bằng đồng hồ đứng yên.
builder.Services.TryAddSingleton(TimeProvider.System);

// Mọi việc liên quan tới ghế (lock RAM hết hạn, đơn Pending quá hạn thanh toán) chạy TRONG process
// Web — bảng lock là in-memory singleton của process đó và chỉ ở đó mới có IHubContext để báo
// SeatUnlocked. Worker chỉ còn email và báo cáo doanh thu, đều làm việc trên DB.

// Trần retry / nhịp quét của hàng đợi email — đọc từ section EmailOutbox.
builder.Services.Configure<EmailOutboxOptions>(
    builder.Configuration.GetSection(EmailOutboxOptions.SectionName));

builder.Services.AddHostedService<EmailOutboxWorker>();
builder.Services.AddHostedService<DailyReportWorker>();

var host = builder.Build();
host.Run();
