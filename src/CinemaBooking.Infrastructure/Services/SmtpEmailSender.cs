using System.Net;
using System.Net.Mail;
using CinemaBooking.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Infrastructure.Services;

/// <summary>
/// Gửi email thật qua SMTP của nhà cung cấp (Gmail app-password, Mailtrap, Amazon SES…).
/// Chỉ được đăng ký khi <c>Email:Smtp:Host</c> có giá trị — xem <see cref="EmailOptions.IsSmtpConfigured"/>.
/// <para>
/// Ném exception khi gửi lỗi để <c>EmailOutboxWorker</c> tăng <c>AttemptCount</c> và thử lại; nuốt lỗi
/// ở đây sẽ đánh dấu email là <c>Sent</c> dù khách không hề nhận được.
/// </para>
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions.SmtpOptions _smtp;
    private readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> log)
    {
        _smtp = options.Value.Smtp;
        _log = log;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        using var mail = new MailMessage
        {
            From = new MailAddress(_smtp.From, _smtp.FromName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = false,
        };
        mail.To.Add(message.To);

        // Một SmtpClient cho mỗi lần gửi: instance dùng chung không thread-safe và outbox gửi song song.
        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            EnableSsl = _smtp.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
        };

        if (!string.IsNullOrWhiteSpace(_smtp.UserName))
            client.Credentials = new NetworkCredential(_smtp.UserName, _smtp.Password);

        await client.SendMailAsync(mail, ct);
        _log.LogInformation("Đã gửi email tới {To} | {Subject}", message.To, message.Subject);
    }
}
