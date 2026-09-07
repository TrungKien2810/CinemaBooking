using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Worker.Workers;

/// <summary>
/// Đọc OutboxEmails ở trạng thái Pending và gửi. Web KHÔNG gửi email trực tiếp —
/// fire-and-forget trong request sẽ mất email nếu process chết giữa chừng.
/// </summary>
public sealed class EmailOutboxWorker : BackgroundService
{
    private const int MaxAttempts = 5;
    private const int BatchSize = 20;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EmailOutboxWorker> _log;

    public EmailOutboxWorker(IServiceScopeFactory scopes, ILogger<EmailOutboxWorker> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(ct))
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();
            var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

            var batch = await db.OutboxEmails
                .Where(e => e.Status == OutboxStatus.Pending && e.AttemptCount < MaxAttempts)
                .OrderBy(e => e.CreatedUtc)
                .Take(BatchSize)
                .ToListAsync(ct);

            // Gửi song song trong batch, nhưng mỗi email tự bắt lỗi để 1 cái hỏng
            // không kéo cả batch chết (Task.WhenAll chỉ ném exception đầu tiên).
            await Task.WhenAll(batch.Select(async mail =>
            {
                try
                {
                    await sender.SendAsync(new EmailMessage(mail.ToAddress, mail.Subject, mail.Body), ct);
                    mail.Status = OutboxStatus.Sent;
                    mail.SentUtc = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    mail.AttemptCount++;
                    mail.LastError = ex.Message[..Math.Min(ex.Message.Length, 1000)];
                    if (mail.AttemptCount >= MaxAttempts) mail.Status = OutboxStatus.Failed;
                    _log.LogWarning(ex, "Gửi email {Id} thất bại lần {N}.", mail.Id, mail.AttemptCount);
                }
            }));

            if (batch.Count > 0) await db.SaveChangesAsync(ct);
        }
    }
}
