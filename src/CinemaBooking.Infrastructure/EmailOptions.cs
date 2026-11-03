using Microsoft.Extensions.Configuration;

namespace CinemaBooking.Infrastructure;

/// <summary>
/// Cấu hình gửi email (section <c>Email</c>). Để trống <see cref="SmtpOptions.Host"/> thì hệ thống
/// dùng <c>LoggingEmailSender</c> (chỉ ghi log) — phù hợp máy dev không có SMTP server; điền Host là
/// tự chuyển sang gửi thật. Mật khẩu nên đặt trong user-secrets / biến môi trường, không commit.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>Đã cấu hình SMTP thật hay chưa (dựa trên section <c>Email:Smtp:Host</c>).</summary>
    public static bool IsSmtpConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config[$"{SectionName}:{nameof(Smtp)}:Host"]);

    public sealed class SmtpOptions
    {
        /// <summary>Máy chủ SMTP. Để trống = chỉ log email ra console.</summary>
        public string Host { get; set; } = string.Empty;

        public int Port { get; set; } = 587;

        /// <summary>STARTTLS/SSL. Cổng 587 thường bật, cổng 25 nội bộ thường tắt.</summary>
        public bool UseSsl { get; set; } = true;

        public string UserName { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        /// <summary>Địa chỉ người gửi hiển thị trong hộp thư của khách.</summary>
        public string From { get; set; } = "no-reply@cinemabooking.local";

        public string FromName { get; set; } = "CinemaBooking";
    }
}
