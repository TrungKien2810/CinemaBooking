using QRCoder;

namespace CinemaBooking.Web.Services;

/// <summary>Tạo ảnh QR (PNG) cho vé để khách in ra hoặc quét lại bằng điện thoại.</summary>
public interface IQrCodeService
{
    byte[] CreatePng(string content, int pixelsPerModule = 8);
}

/// <summary>
/// Dùng QRCoder (thuần managed, không cần System.Drawing) để chạy được cả trên Linux/Docker.
/// Mức sửa lỗi Q đủ cho ảnh in vé; nội dung là URL xác nhận vé.
/// </summary>
public sealed class QrCodeService : IQrCodeService
{
    public byte[] CreatePng(string content, int pixelsPerModule = 8)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Nội dung QR không được rỗng.", nameof(content));

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }
}
