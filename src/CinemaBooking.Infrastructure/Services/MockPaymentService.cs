using CinemaBooking.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace CinemaBooking.Infrastructure.Services;

/// <summary>
/// Cổng thanh toán giả lập. HttpClient được TIÊM VÀO qua IHttpClientFactory
/// (đăng ký bằng AddHttpClient) — không bao giờ new HttpClient trong service.
/// </summary>
public sealed class MockPaymentService : IPaymentService
{
    private readonly HttpClient _http;
    private readonly ILogger<MockPaymentService> _log;

    public MockPaymentService(HttpClient http, ILogger<MockPaymentService> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct = default)
    {
        // TODO(Sprint 3): gọi endpoint giả lập, xử lý timeout + retry qua Polly.
        _log.LogInformation("Charging {Amount} for {Code}", request.Amount, request.BookingCode);
        await Task.Delay(200, ct);
        return new PaymentResult(true, $"TX-{Guid.NewGuid():N}"[..14], null);
    }
}
