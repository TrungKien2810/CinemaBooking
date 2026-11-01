using System.Net;
using System.Security.Cryptography;
using System.Text;
using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CinemaBooking.Tests;

/// <summary>
/// Checksum VNPAY phải tính đúng như tài liệu/demo chính thức (PHP <c>urlencode()</c>, Java
/// <c>URLEncoder</c>): tham số sort tăng dần, nối bằng '&amp;', giá trị encode kiểu form
/// (dấu cách = <c>+</c>, KHÔNG phải <c>%20</c>), HMACSHA512.
/// <para>
/// Sai một byte là cổng thanh toán trả "Sai chữ ký" ngay khi mở trang thanh toán — lỗi từng xảy ra
/// vì code dùng <see cref="Uri.EscapeDataString(string)"/> cho <c>vnp_OrderInfo</c> có dấu cách.
/// </para>
/// </summary>
public sealed class VnpayPaymentServiceTests
{
    // Không dùng HashSecret thật trong test: chỉ cần một chuỗi cố định để ký/kiểm tra.
    private const string Secret = "TEST_SECRET_FOR_UNIT_TESTS_ONLY";

    private static readonly VnpayOptions Options = new()
    {
        TmnCode = "TESTCODE",
        HashSecret = Secret,
        PaymentUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
        ReturnUrl = "http://localhost:5005/Booking/PaymentReturn",
    };

    private static VnpayPaymentRequest Request() => new(
        TxnRef: "CB-ABC123",
        Amount: 180_000m,
        OrderInfo: "Thanh toan don hang CB-ABC123",
        ReturnUrl: Options.ReturnUrl,
        TmnCode: Options.TmnCode,
        HashSecret: Secret,
        IpAddr: "127.0.0.1");

    private static VnpayPaymentService Sut() =>
        new(Microsoft.Extensions.Options.Options.Create(Options), NullLogger<VnpayPaymentService>.Instance);

    [Fact]
    public async Task Payment_url_signs_the_sorted_params_exactly_like_the_vnpay_demo()
    {
        var result = await Sut().CreateVnpayPaymentAsync(Request());

        Assert.True(result.Success);
        Assert.StartsWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?", result.PaymentUrl);

        var query = new Uri(result.PaymentUrl!).Query.TrimStart('?');

        // Tiền VND x100; OrderInfo đã bỏ dấu cách nên checksum không phụ thuộc cách encode khoảng trắng.
        Assert.Contains("vnp_Amount=18000000", query);
        Assert.Contains("vnp_TxnRef=CB-ABC123", query);
        Assert.Contains("vnp_OrderInfo=Thanh-toan-don-hang-CB-ABC123", query);
        Assert.DoesNotContain("%20", query);

        // Chữ ký = HMACSHA512 của ĐÚNG chuỗi tham số đã gửi (không gồm vnp_SecureHash).
        var signed = query[..query.IndexOf("&vnp_SecureHash=", StringComparison.Ordinal)];
        Assert.EndsWith("vnp_SecureHash=" + Hmac(signed), query);
    }

    [Fact]
    public async Task Return_url_params_signed_by_vnpay_are_accepted_and_tampering_is_rejected()
    {
        var response = new Dictionary<string, string?>
        {
            ["vnp_Amount"] = "18000000",
            ["vnp_BankCode"] = "NCB",
            ["vnp_OrderInfo"] = "Thanh toan don hang CB-ABC123", // có dấu cách -> '+' khi ký
            ["vnp_ResponseCode"] = "00",
            ["vnp_TmnCode"] = "TESTCODE",
            ["vnp_TransactionNo"] = "14000001",
            ["vnp_TxnRef"] = "CB-ABC123",
        };
        response["vnp_SecureHash"] = Sign(response);

        var verify = await Sut().VerifyVnpayPaymentAsync(Request(), response);

        Assert.True(verify.SignatureValid);
        Assert.True(verify.PaymentSucceeded);
        Assert.Equal(180_000m, verify.Amount);
        Assert.Equal("CB-ABC123", verify.TxnRef);

        // Sửa bất kỳ tham số nào sau khi ký -> chữ ký phải bị từ chối.
        response["vnp_ResponseCode"] = "24";
        var tampered = await Sut().VerifyVnpayPaymentAsync(Request(), response);
        Assert.False(tampered.SignatureValid);
        Assert.False(tampered.PaymentSucceeded);
    }

    private static string Sign(IReadOnlyDictionary<string, string?> response)
    {
        var canonical = string.Join("&", response
            .Where(kv => kv.Key is not ("vnp_SecureHash" or "vnp_SecureHashType"))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value ?? string.Empty)}"));

        return Hmac(canonical);
    }

    private static string Hmac(string data) =>
        Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(data)))
            .ToLowerInvariant();
}
