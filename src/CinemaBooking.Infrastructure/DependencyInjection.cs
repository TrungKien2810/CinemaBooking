using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CinemaBooking.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCinemaInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<CinemaDbContext>(o =>
            o.UseSqlServer(config.GetConnectionString("Default")));

        // Dịch vụ hạ tầng dùng TimeProvider để test được đồng hồ; Web/Worker có thể override sau.
        services.TryAddSingleton(TimeProvider.System);

        // Cấu hình sai (0 phút) thì chết ngay lúc khởi động, không đợi tới khi khách chọn ghế.
        services.AddOptions<SeatHoldOptions>()
            .Bind(config.GetSection(SeatHoldOptions.SectionName))
            .Validate(o => o.DurationMinutes is >= 1 and <= 30, "SeatHold:DurationMinutes phải trong khoảng 1–30.")
            .ValidateOnStart();

        // Scoped + HttpClientFactory: tránh socket exhaustion và stale DNS.
        services.AddHttpClient<IPaymentService, MockPaymentService>(c =>
        {
            c.BaseAddress = new Uri(config["Payment:BaseUrl"] ?? "https://localhost/mock-pay/");
            c.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddOptions<VnpayOptions>()
            .Bind(config.GetSection(VnpayOptions.SectionName));

        services.AddScoped<IVnpayPaymentService, VnpayPaymentService>();

        // Có Email:Smtp:Host thì gửi thật, chưa cấu hình thì chỉ log (dev). Đổi cấu hình là đổi
        // hành vi, không phải sửa code; outbox worker chỉ phụ thuộc IEmailSender.
        services.AddOptions<EmailOptions>().Bind(config.GetSection(EmailOptions.SectionName));
        if (EmailOptions.IsSmtpConfigured(config))
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        else
            services.AddScoped<IEmailSender, LoggingEmailSender>();

        // Singleton: không giữ trạng thái, chỉ gọi RNG mật mã.
        services.AddSingleton<IBookingCodeGenerator, BookingCodeGenerator>();

        return services;
    }
}
