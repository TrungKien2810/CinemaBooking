using System.Globalization;
using System.Text;

namespace CinemaBooking.Web.Services;

/// <summary>
/// So khớp tìm kiếm không phân biệt hoa/thường và DẤU tiếng Việt: gõ "can tho" vẫn ra "Cần Thơ", "dung" ra "Đúng".
/// Dùng cho các danh sách nhỏ lọc trong bộ nhớ (chi nhánh, tài khoản...). Danh sách lớn lọc ở SQL.
/// </summary>
public static class TextSearch
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var decomposed = text.Trim().Replace('Đ', 'D').Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Mọi từ trong <paramref name="query"/> đều xuất hiện trong ít nhất một chuỗi của <paramref name="haystacks"/>.</summary>
    public static bool Matches(string? query, params string?[] haystacks)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;

        var text = string.Join(' ', haystacks.Select(Normalize));
        return words.All(text.Contains);
    }
}
