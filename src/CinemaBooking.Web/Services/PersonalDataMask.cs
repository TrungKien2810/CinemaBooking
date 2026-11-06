namespace CinemaBooking.Web.Services;

/// <summary>
/// Che thông tin cá nhân trên trang vé công khai. Mã vé nằm trong QR/email nên ai cầm mã cũng mở được
/// trang xác nhận; họ tên, email, số điện thoại đầy đủ chỉ dành cho chủ vé và nhân viên rạp.
/// </summary>
public static class PersonalDataMask
{
    /// <summary>"Nguyễn Văn An" → "N*** V*** A***".</summary>
    public static string Name(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', words.Select(w => w[0] + "***"));
    }

    /// <summary>"an.nguyen@example.com" → "a***@example.com".</summary>
    public static string Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        return email[0] + "***" + email[at..];
    }

    /// <summary>"0901234567" → "*******567" (chỉ giữ 3 số cuối).</summary>
    public static string? Phone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var p = phone.Trim();
        if (p.Length <= 3) return new string('*', p.Length);
        return new string('*', p.Length - 3) + p[^3..];
    }
}

/// <summary>
/// Các mã vé khách vừa đặt trong phiên này. Khách guest (không đăng nhập) vừa thanh toán xong vẫn
/// phải thấy thông tin đầy đủ của chính vé mình — chỉ người chỉ có mã mới bị che.
/// </summary>
public static class OwnedBookingSessionExtensions
{
    private const string Key = "Booking.Owned";
    private const int Max = 20;

    public static void RememberOwnedBooking(this ISession session, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        var codes = Read(session);
        codes.RemoveAll(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
        codes.Add(code.Trim().ToUpperInvariant());
        if (codes.Count > Max) codes.RemoveRange(0, codes.Count - Max);
        session.SetString(Key, string.Join(',', codes));
    }

    public static bool OwnsBooking(this ISession session, string code) =>
        Read(session).Contains(code.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);

    private static List<string> Read(ISession session) =>
        (session.GetString(Key) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
}
