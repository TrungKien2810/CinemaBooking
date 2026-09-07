using CinemaBooking.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace CinemaBooking.Infrastructure.Services;

/// <summary>Bản cắm tạm cho môi trường dev: chỉ log. Sprint 4 thay bằng SmtpEmailSender.</summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _log;
    public LoggingEmailSender(ILogger<LoggingEmailSender> log) => _log = log;

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _log.LogInformation("EMAIL -> {To} | {Subject}", message.To, message.Subject);
        return Task.CompletedTask;
    }
}
