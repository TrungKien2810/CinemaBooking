using System.Security.Cryptography;
using CinemaBooking.Domain.Abstractions;

namespace CinemaBooking.Infrastructure.Services;

/// <summary>
/// Mã ngẫu nhiên bằng RNG mật mã, không phải bộ đếm: mã tuần tự sẽ để lộ số vé đã bán và
/// cho phép dò đơn của người khác qua trang tra cứu.
/// </summary>
public sealed class BookingCodeGenerator : IBookingCodeGenerator
{
    public const string Prefix = "CB-";
    public const int RandomLength = 6;

    /// <summary>
    /// Bảng chữ Crockford Base32: bỏ I, L, O, U và 0, 1 để khách không đọc nhầm giữa
    /// O/0, I/1/L qua điện thoại. 30^6 ≈ 729 triệu tổ hợp — đủ thưa cho quy mô đề tài.
    /// </summary>
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    public string Next()
    {
        Span<char> code = stackalloc char[Prefix.Length + RandomLength];
        Prefix.AsSpan().CopyTo(code);

        for (var i = 0; i < RandomLength; i++)
        {
            // GetInt32 lấy mẫu không lệch (rejection sampling), khác với % gây thiên vị.
            code[Prefix.Length + i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(code);
    }
}
