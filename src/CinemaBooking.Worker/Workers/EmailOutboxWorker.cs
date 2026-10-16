using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Worker.Workers;

/// <summary>
/// Đọc OutboxEmails ở trạng thái Pending và gửi. Web KHÔNG gửi email trực tiếp —
/// fire-and-forget trong request sẽ mất email nếu process chết giữa chừng.
/// <para>
/// Retry có trần (<see cref="EmailOutboxOptions.MaxAttempts"/>): email hỏng được đánh dấu Failed
/// thay vì quay vòng mãi và chặn các email phía sau.
/// </para>
/// </summary>
public sealed class EmailOutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly EmailOutboxOptions _options;
    private readonly ILogger<EmailOutboxWorker> _log;

    public EmailOutboxWorker(
        IServiceScopeFactory scopes,
        IOptions<EmailOutboxOptions> options,
        ILogger<EmailOutboxWorker> log)
    {
        _scopes = scopes;
        _options = options.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollSeconds));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await DrainOnceAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Lỗi khi xử lý outbox email — vòng lặp vẫn tiếp tục.");
            }
        }
    }

    private async Task DrainOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var batch = await db.OutboxEmails
            .Where(e => e.Status == OutboxStatus.Pending && e.AttemptCount < _options.MaxAttempts)
            .OrderBy(e => e.CreatedUtc)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        // Gửi song song trong batch, nhưng mỗi email tự bắt lỗi để 1 cái hỏng không kéo cả batch chết
        // (Task.WhenAll chỉ ném exception đầu tiên).
        await Task.WhenAll(batch.Select(mail => DeliverAsync(sender, mail, ct)));
        await db.SaveChangesAsync(ct);
    }

    private async Task DeliverAsync(IEmailSender sender, OutboxEmail mail, CancellationToken ct)
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
            if (mail.AttemptCount >= _options.MaxAttempts) mail.Status = OutboxStatus.Failed;
            _log.LogWarning(ex, "Gửi email {Id} thất bại lần {Attempt}/{Max}.", mail.Id, mail.AttemptCount, _options.MaxAttempts);
        }
    }
}
