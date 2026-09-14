using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Logging;
using CinemaBooking.Worker.Workers;

var builder = Host.CreateApplicationBuilder(args);

// Cùng cấu hình log với Web: Development ra console một dòng, ngoài Development ra JSON.
builder.Logging.AddCinemaLogging(builder.Environment.IsDevelopment());

builder.Services.AddCinemaInfrastructure(builder.Configuration);

builder.Services.AddHostedService<ExpiredSeatCleaner>();
builder.Services.AddHostedService<EmailOutboxWorker>();
builder.Services.AddHostedService<DailyReportWorker>();

var host = builder.Build();
host.Run();
