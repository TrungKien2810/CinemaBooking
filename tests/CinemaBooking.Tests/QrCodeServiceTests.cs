using CinemaBooking.Web.Services;

namespace CinemaBooking.Tests;

public sealed class QrCodeServiceTests
{
    [Fact]
    public void Creates_a_valid_png_for_a_ticket_url()
    {
        var png = new QrCodeService().CreatePng(
            "http://localhost:5005/Booking/Confirmation?code=CB-TEST01");

        Assert.True(png.Length > 100);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8));
    }

    [Fact]
    public void Rejects_empty_content()
    {
        Assert.Throws<ArgumentException>(() => new QrCodeService().CreatePng(" "));
    }
}
