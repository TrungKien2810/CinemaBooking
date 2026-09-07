namespace CinemaBooking.Domain.Abstractions;

public sealed record PaymentRequest(string BookingCode, decimal Amount, string CustomerEmail);
public sealed record PaymentResult(bool Succeeded, string? TransactionId, string? FailureReason);

public interface IPaymentService
{
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct = default);
}

public sealed record EmailMessage(string To, string Subject, string Body);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
