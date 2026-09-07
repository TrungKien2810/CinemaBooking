using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CinemaBooking.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCinemaInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<CinemaDbContext>(o =>
            o.UseSqlServer(config.GetConnectionString("Default")));

        // Singleton: trạng thái lock phải sống xuyên request.
        services.AddSingleton<ISeatLockManager, InMemorySeatLockManager>();

        // Scoped + HttpClientFactory: tránh socket exhaustion và stale DNS.
        services.AddHttpClient<IPaymentService, MockPaymentService>(c =>
        {
            c.BaseAddress = new Uri(config["Payment:BaseUrl"] ?? "https://localhost/mock-pay/");
            c.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddScoped<IEmailSender, LoggingEmailSender>();

        return services;
    }
}
