using System.Collections.Generic;

namespace CinemaBooking.Domain.Abstractions;

public sealed record PaymentRequest(string BookingCode, decimal Amount, string CustomerEmail);
public sealed record PaymentResult(bool Succeeded, string? TransactionId, string? FailureReason);

/// <summary>
/// Yêu cầu tạo giao dịch thanh toán VNPAY cho một đơn đặt vé. <c>TxnRef</c> là mã đơn hàng tại
/// hệ thống merchant (trong project là <c>BookingCode</c>) — không được trùng trong ngày.
/// </summary>
public sealed record VnpayPaymentRequest(
    string TxnRef,
    decimal Amount,
    string OrderInfo,
    string ReturnUrl,
    string TmnCode,
    string HashSecret,
    string IpAddr = "127.0.0.1",
    string Locale = "vn",
    string OrderType = "other",
    int ExpireMinutes = 15);

/// <summary>
/// Kết quả tạo URL thanh toán VNPAY (cổng redirect: khách được đưa sang
/// <c>vnp_Url?vnp_...&amp;vnp_SecureHash=...</c> để thanh toán online).
/// </summary>
public sealed record VnpayPaymentResult(bool Success, string? PaymentUrl, string? QrContent, string? TxnRef, string? ResponseCode, string? TransactionId, string? BankCode, string? FailureReason, string? CreateDate = null);

/// <summary>
/// Kết quả xác nhận kết quả trả về từ VNPAY (qua ReturnUrl/IPN): đã kiểm tra chữ ký
/// <c>vnp_SecureHash</c> (HMACSHA512) chưa và giao dịch có thành công hay không.
/// <c>PayDate</c> là <c>vnp_PayDate</c> gốc (GMT+7) — cần lưu lại để đối soát/hoàn tiền.
/// </summary>
public sealed record VnpayVerifyResult(
    bool SignatureValid,
    bool PaymentSucceeded,
    string? TxnRef,
    string? ResponseCode,
    string? TransactionId,
    string? BankCode,
    decimal Amount,
    string? FailureReason,
    string? PayDate = null);

public interface IVnpayPaymentService
{
    Task<VnpayPaymentResult> CreateVnpayPaymentAsync(VnpayPaymentRequest request, CancellationToken ct = default);

    Task<VnpayVerifyResult> VerifyVnpayPaymentAsync(VnpayPaymentRequest request, IDictionary<string, string?> response, CancellationToken ct = default);
}

public interface IPaymentService
{
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct = default);
}

public sealed record EmailMessage(string To, string Subject, string Body);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
