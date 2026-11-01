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

        // Singleton: trạng thái lock phải sống xuyên request.
        services.AddSingleton<ISeatLockManager, InMemorySeatLockManager>();

        // Cấu hình sai (0 phút, quét 0 giây) thì chết ngay lúc khởi động, không đợi tới khi khách chọn ghế.
        services.AddOptions<SeatHoldOptions>()
            .Bind(config.GetSection(SeatHoldOptions.SectionName))
            .Validate(o => o.DurationMinutes is >= 1 and <= 30, "SeatHold:DurationMinutes phải trong khoảng 1–30.")
            .Validate(o => o.CleanupIntervalSeconds is >= 1 and <= 300, "SeatHold:CleanupIntervalSeconds phải trong khoảng 1–300.")
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

        // Refund service dùng named client thay vì typed client: tránh ActivatorUtilities phải
        // dựng constructor có TimeProvider/IOptions, đồng thời vẫn reuse HttpClient handler.
        services.AddHttpClient(nameof(VnpayRefundService), c =>
        {
            c.Timeout = TimeSpan.FromSeconds(20);
            // VNPAY chặn 403 (openresty/WAF) nếu request không có User-Agent; HttpClient mặc định
            // không gửi header này. Đặt UA cố định để API refund nhận request.
            c.DefaultRequestHeaders.UserAgent.ParseAdd("CinemaBooking/1.0");
        });
        services.AddScoped<IVnpayRefundService, VnpayRefundService>();

        services.AddScoped<IEmailSender, LoggingEmailSender>();

        // Singleton: không giữ trạng thái, chỉ gọi RNG mật mã.
        services.AddSingleton<IBookingCodeGenerator, BookingCodeGenerator>();

        return services;
    }
}
