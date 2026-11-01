using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CinemaBooking.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Infrastructure.Services;

/// <summary>
/// Cổng thanh toán VNPAY (môi trường Sandbox khi cấu hình <c>Vnpay:Environment=Sandbox</c>).
/// Hai bước chính của luồng đặt vé online:
/// <list type="bullet">
/// <item><c>CreateVnpayPaymentAsync</c>: dựng URL thanh toán <c>vnp_Url?vnp_...&amp;vnp_SecureHash=...</c>
/// theo đúng thuật toán của VNPAY (sort key tăng dần, HMACSHA512) rồi trả URL cho khách hàng.</item>
/// <item><c>VerifyVnpayPaymentAsync</c>: kiểm tra chữ ký <c>vnp_SecureHash</c> của phản hồi từ VNPAY
/// (ReturnUrl sau khi khách thanh toán xong, hoặc IPN notify) trước khi xác nhận kết quả.</item>
/// </list>
/// Luồng chuyển hướng: khách hàng được đưa sang cổng VNPAY → VNPAY chuyển về <c>ReturnUrl</c> với
/// các tham số <c>vnp_*</c> → merchant xác minh chữ ký → cập nhật trạng thái đơn.
/// </summary>
public sealed class VnpayPaymentService : IVnpayPaymentService
{
    private readonly VnpayOptions _options;
    private readonly ILogger<VnpayPaymentService> _log;

    public VnpayPaymentService(IOptions<VnpayOptions> options, ILogger<VnpayPaymentService> log)
    {
        _options = options.Value;
        _log = log;
    }

    /// <summary>
    /// Tạo URL thanh toán VNPAY cho một đơn hàng. Số tiền gửi lên VNPAY phải là VND chưa phân
    /// tách thập phân nhân 100 (cents); <c>vnp_TxnRef</c> là mã đơn hàng — không được trùng trong ngày.
    /// </summary>
    public Task<VnpayPaymentResult> CreateVnpayPaymentAsync(VnpayPaymentRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.TmnCode) || string.IsNullOrWhiteSpace(_options.HashSecret))
        {
            return Task.FromResult(new VnpayPaymentResult(
                Success: false,
                PaymentUrl: null,
                QrContent: null,
                TxnRef: request.TxnRef,
                ResponseCode: null,
                TransactionId: null,
                BankCode: null,
                FailureReason: "Chưa cấu hình vnpay:TmnCode hoặc vnpay:HashSecret."));
        }

        var createDate = DateTime.UtcNow.AddHours(7); // VNPAY yêu cầu giờ GMT+7 (múi giờ Việt Nam).
        var createDateStr = createDate.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var expireDateStr = createDate.AddMinutes(request.ExpireMinutes).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        var pairs = new List<KeyValuePair<string, string>>
        {
            new("vnp_Amount", ((long)Math.Round(request.Amount * 100)).ToString(CultureInfo.InvariantCulture)),
            new("vnp_Command", "pay"),
            new("vnp_CreateDate", createDateStr),
            new("vnp_CurrCode", "VND"),
            new("vnp_IpAddr", request.IpAddr),
            new("vnp_Locale", request.Locale),
            new("vnp_OrderInfo", SanitizeOrderInfo(request.OrderInfo)),
            new("vnp_OrderType", string.IsNullOrWhiteSpace(request.OrderType) ? "other" : request.OrderType),
            new("vnp_ReturnUrl", request.ReturnUrl),
            new("vnp_TmnCode", request.TmnCode),
            new("vnp_TxnRef", request.TxnRef),
            new("vnp_ExpireDate", expireDateStr),
            new("vnp_Version", "2.1.0"),
        };

        // Dữ liệu checksum = các cặp key=value (key tăng dần theo thứ tự bảng chữ cái) nối với '&'.
        var ordered = pairs.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        var hashData = BuildQueryString(ordered);
        var secureHash = ComputeHmacSha512Hex(request.HashSecret, hashData);
        var paymentUrl = $"{_options.PaymentUrl.TrimEnd('/')}?{BuildQueryString(ordered)}&vnp_SecureHash={secureHash}";

        return Task.FromResult(new VnpayPaymentResult(
            Success: true,
            PaymentUrl: paymentUrl,
            QrContent: null,
            TxnRef: request.TxnRef,
            ResponseCode: "00",
            TransactionId: null,
            BankCode: null,
            FailureReason: null,
            CreateDate: createDateStr));
    }

    /// <summary>
    /// Kiểm tra phản hồi của VNPAY (ReturnUrl sau thanh toán, hoặc IPN notify): toàn vẹn dữ liệu
    /// qua <c>vnp_SecureHash</c> (HMACSHA512, hash data gồm các tham số còn lại đã sort) trước khi
    /// xét kết quả <c>vnp_ResponseCode</c> ("00" = thành công).
    /// </summary>
    public Task<VnpayVerifyResult> VerifyVnpayPaymentAsync(VnpayPaymentRequest request, IDictionary<string, string?> response, CancellationToken ct = default)
    {
        var txnRef = response["vnp_TxnRef"];
        var responseCode = response["vnp_ResponseCode"];
        var transactionId = response["vnp_TransactionNo"];
        var bankCode = response["vnp_BankCode"];
        var amountRaw = response["vnp_Amount"];
        var payDate = response.TryGetValue("vnp_PayDate", out var payDateValue) ? payDateValue : null;

        var data = response
            .Where(kv => !kv.Key.Equals("vnp_SecureHash", StringComparison.OrdinalIgnoreCase)
                        && !kv.Key.Equals("vnp_SecureHashType", StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value ?? string.Empty))
            .ToList();

        var hashData = BuildQueryString(data);
        var computedHash = ComputeHmacSha512Hex(request.HashSecret, hashData);
        var signatureValid = response["vnp_SecureHash"] is not null
            && string.Equals(computedHash, response["vnp_SecureHash"], StringComparison.OrdinalIgnoreCase);

        if (!signatureValid)
        {
            return Task.FromResult(new VnpayVerifyResult(
                SignatureValid: false,
                PaymentSucceeded: false,
                TxnRef: txnRef,
                ResponseCode: responseCode,
                TransactionId: transactionId,
                BankCode: bankCode,
                Amount: ParseAmount(amountRaw),
                FailureReason: "Chu ky vnp_SecureHash không hợp lệ.",
                PayDate: payDate));
        }

        var amount = ParseAmount(amountRaw);
        var paymentSucceeded = string.Equals(responseCode, "00", StringComparison.Ordinal);

        return Task.FromResult(new VnpayVerifyResult(
            SignatureValid: true,
            PaymentSucceeded: paymentSucceeded,
            TxnRef: txnRef,
            ResponseCode: responseCode,
            TransactionId: transactionId,
            BankCode: bankCode,
            Amount: amount,
            FailureReason: null,
            PayDate: payDate));
    }

    private static string SanitizeOrderInfo(string orderInfo)
    {
        // VNPAY yêu cầu OrderInfo tiếng Việt không dấu, không ký tự đặc biệt. Dấu cách được đổi
        // thành '-' để chuỗi checksum không phụ thuộc vào cách encode khoảng trắng (+ hay %20).
        var normalized = orderInfo.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')
                sb.Append(ch);
            else if (ch == ' ')
                sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>
    /// Dựng chuỗi tham số cho cả checksum lẫn query string — hai bên phải giống NHAU từng byte.
    /// Dùng <see cref="WebUtility.UrlEncode"/> (dấu cách = <c>+</c>) đúng như demo chính thức của
    /// VNPAY (PHP <c>urlencode()</c>, Java <c>URLEncoder</c>). Dùng <see cref="Uri.EscapeDataString(string)"/>
    /// (dấu cách = <c>%20</c>) sẽ làm VNPAY báo "Sai chữ ký".
    /// </summary>
    private static string BuildQueryString(IEnumerable<KeyValuePair<string, string>> pairs) =>
        string.Join("&", pairs.Select(p => $"{p.Key}={WebUtility.UrlEncode(p.Value)}"));

    private static decimal ParseAmount(string? amountRaw)
    {
        // vnp_Amount gửi lên VNPAY là VND nhân 100 (cents); VNPAY phản hồi cũng theo đơn vị đó.
        return decimal.TryParse(amountRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? Math.Round(amount) / 100m
            : 0m;
    }

    private static string ComputeHmacSha512Hex(string secret, string hashData)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(hashData));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
