using CinemaBooking.Infrastructure;
using CinemaBooking.Worker.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddCinemaInfrastructure(builder.Configuration);

builder.Services.AddHostedService<ExpiredSeatCleaner>();
builder.Services.AddHostedService<EmailOutboxWorker>();
builder.Services.AddHostedService<DailyReportWorker>();

var host = builder.Build();
host.Run();
