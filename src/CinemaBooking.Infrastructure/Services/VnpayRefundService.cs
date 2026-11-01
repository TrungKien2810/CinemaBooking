using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CinemaBooking.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Infrastructure.Services;

/// <summary>
/// Gọi API hoàn tiền VNPAY (server-to-server, POST JSON):
/// <c>https://sandbox.vnpayment.vn/merchant_webapi/api/transaction</c>.
/// <para>
/// Chuỗi checksum của REQUEST nối bằng <c>|</c> theo đúng thứ tự tài liệu:
/// <c>RequestId|Version|Command|TmnCode|TransactionType|TxnRef|Amount|TransactionNo|
/// TransactionDate|CreateBy|CreateDate|IpAddr|OrderInfo</c>.
/// Response cũng phải được kiểm tra chữ ký trước khi tin <c>vnp_ResponseCode</c>.
/// </para>
/// </summary>
public sealed class VnpayRefundService : IVnpayRefundService
{
    private readonly VnpayOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<VnpayRefundService> _log;

    public VnpayRefundService(
        IOptions<VnpayOptions> options,
        IHttpClientFactory httpFactory,
        TimeProvider clock,
        ILogger<VnpayRefundService> log)
    {
        _options = options.Value;
        _httpFactory = httpFactory;
        _clock = clock;
        _log = log;
    }

    public async Task<VnpayRefundResult> RefundAsync(VnpayRefundRequest request, CancellationToken ct = default)
    {
        if (!_options.RefundEnabled)
        {
            return new VnpayRefundResult(
                Success: false,
                ResponseCode: null,
                TransactionStatus: null,
                RefundTransactionNo: null,
                Message: "Chưa bật Vnpay:RefundEnabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.TmnCode) || string.IsNullOrWhiteSpace(_options.HashSecret))
        {
            return new VnpayRefundResult(false, null, null, null, "Chưa cấu hình TmnCode/HashSecret.");
        }

        var amount = ((long)Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero))
            .ToString(CultureInfo.InvariantCulture);
        var transactionNo = request.TransactionNo ?? string.Empty;
        var orderInfo = ToAscii(request.OrderInfo);
        var createDate = _clock.GetUtcNow().UtcDateTime.AddHours(7)
            .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var transactionType = request.FullRefund ? "02" : "03";

        var hashData = string.Join("|",
            request.RequestId,
            "2.1.0",
            "refund",
            _options.TmnCode,
            transactionType,
            request.TxnRef,
            amount,
            transactionNo,
            request.TransactionDate,
            request.CreateBy,
            createDate,
            request.IpAddr,
            orderInfo);

        var secureHash = HmacSha512(_options.HashSecret, hashData);

        var body = new Dictionary<string, string>
        {
            ["vnp_RequestId"] = request.RequestId,
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "refund",
            ["vnp_TmnCode"] = _options.TmnCode,
            ["vnp_TransactionType"] = transactionType,
            ["vnp_TxnRef"] = request.TxnRef,
            ["vnp_Amount"] = amount,
            ["vnp_OrderInfo"] = orderInfo,
            ["vnp_TransactionNo"] = transactionNo,
            ["vnp_TransactionDate"] = request.TransactionDate,
            ["vnp_CreateBy"] = request.CreateBy,
            ["vnp_CreateDate"] = createDate,
            ["vnp_IpAddr"] = request.IpAddr,
            ["vnp_SecureHash"] = secureHash,
        };

        try
        {
            var client = _httpFactory.CreateClient(nameof(VnpayRefundService));
            using var response = await client.PostAsJsonAsync(_options.RefundUrl, body, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("VNPAY refund HTTP {Status}: {Body}", (int)response.StatusCode, raw);
                return new VnpayRefundResult(false, null, null, null,
                    $"VNPAY trả HTTP {(int)response.StatusCode}.", raw);
            }

            using var json = JsonDocument.Parse(raw);
            var root = json.RootElement;

            string? Get(string name) =>
                root.TryGetProperty(name, out var value) ? value.ToString() : null;

            var responseCode = Get("vnp_ResponseCode");
            var transactionStatus = Get("vnp_TransactionStatus");
            var refundTransactionNo = Get("vnp_TransactionNo");
            var message = Get("vnp_Message");
            var responseHash = Get("vnp_SecureHash");

            // Response cũng phải ký HMACSHA512; nếu VNPAY trả thiếu hash thì không tin kết quả.
            if (!string.IsNullOrWhiteSpace(responseHash))
            {
                var responseHashData = string.Join("|",
                    Get("vnp_ResponseId") ?? string.Empty,
                    Get("vnp_Command") ?? string.Empty,
                    responseCode ?? string.Empty,
                    message ?? string.Empty,
                    Get("vnp_TmnCode") ?? string.Empty,
                    Get("vnp_TxnRef") ?? string.Empty,
                    Get("vnp_Amount") ?? string.Empty,
                    Get("vnp_BankCode") ?? string.Empty,
                    Get("vnp_PayDate") ?? string.Empty,
                    refundTransactionNo ?? string.Empty,
                    Get("vnp_TransactionType") ?? string.Empty,
                    transactionStatus ?? string.Empty,
                    Get("vnp_OrderInfo") ?? string.Empty);

                var expected = HmacSha512(_options.HashSecret, responseHashData);
                if (!string.Equals(expected, responseHash, StringComparison.OrdinalIgnoreCase))
                {
                    _log.LogWarning("VNPAY refund response sai chữ ký: {Body}", raw);
                    return new VnpayRefundResult(false, responseCode, transactionStatus, refundTransactionNo,
                        "Chữ ký phản hồi VNPAY không hợp lệ.", raw);
                }
            }
            else
            {
                _log.LogWarning("VNPAY refund response thiếu vnp_SecureHash: {Body}", raw);

                // Phản hồi lỗi của VNPAY có thể không ký; nhưng TUYỆT ĐỐI không tin một phản hồi
                // báo thành công mà thiếu chữ ký.
                if (string.Equals(responseCode, "00", StringComparison.Ordinal))
                {
                    return new VnpayRefundResult(false, responseCode, transactionStatus,
                        refundTransactionNo, "Phản hồi hoàn tiền của VNPAY thiếu chữ ký.", raw);
                }
            }

            var success = string.Equals(responseCode, "00", StringComparison.Ordinal);
            return new VnpayRefundResult(success, responseCode, transactionStatus,
                refundTransactionNo,
                success ? (message ?? "OK") : DescribeRefundError(responseCode),
                raw);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _log.LogError(ex, "Lỗi gọi VNPAY refund cho {TxnRef}", request.TxnRef);
            return new VnpayRefundResult(false, null, null, null,
                ex is TaskCanceledException ? "VNPAY không phản hồi kịp." : "Không gọi được VNPAY.");
        }
    }

    /// <summary>Đổi mã lỗi VNPAY sang câu tiếng Việt hiển thị cho khách; mã gốc vẫn lưu ở DB/log.</summary>
    private static string DescribeRefundError(string? code) => code switch
    {
        "02" => "Merchant không hợp lệ (kiểm tra lại TmnCode).",
        "03" => "Dữ liệu gửi VNPAY không đúng định dạng.",
        "08" => "VNPAY đang bảo trì, vui lòng thử lại sau.",
        "16" => "VNPAY không cho phép hoàn tiền vào lúc này.",
        "91" => "Không tìm thấy giao dịch gốc trên VNPAY.",
        "93" => "Số tiền hoàn không hợp lệ.",
        "94" => "Giao dịch đã được gửi yêu cầu hoàn tiền trước đó.",
        "97" => "Chữ ký yêu cầu hoàn tiền không hợp lệ.",
        "99" => "VNPAY báo lỗi không xác định (mã 99). Vui lòng thử lại sau.",
        _ => $"VNPAY từ chối hoàn tiền{(string.IsNullOrWhiteSpace(code) ? "." : $" (mã {code}).")}",
    };

    private static string HmacSha512(string secret, string data)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    /// <summary>VNPAY yêu cầu OrderInfo không dấu, không ký tự đặc biệt.</summary>
    private static string ToAscii(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' or ' ')
                sb.Append(ch);
        }
        var result = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? "Hoan tien ve" : result;
    }
}
