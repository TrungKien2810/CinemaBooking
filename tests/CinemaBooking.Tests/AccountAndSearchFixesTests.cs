using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain.Abstractions;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Services;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CinemaBooking.Tests;

/// <summary>Các lỗi tìm ra khi rà soát: SĐT hồ sơ, tìm phim không dấu, callback VNPAY thiếu tham số.</summary>
public class AccountAndSearchFixesTests
{
    private static bool IsValid(ProfileViewModel model) =>
        Validator.TryValidateObject(model, new ValidationContext(model), new List<ValidationResult>(), validateAllProperties: true);

    [Theory]
    [InlineData("12", false)]
    [InlineData("abc", false)]
    [InlineData("0912345678", true)]
    [InlineData("+84 912 345 678", true)]
    [InlineData(null, true)]
    public void Profile_phone_must_look_like_a_phone_number(string? phone, bool expected)
    {
        var model = new ProfileViewModel { FullName = "An", PhoneNumber = phone };

        Assert.Equal(expected, IsValid(model));
    }

    [Theory]
    [InlineData("mat biec")]
    [InlineData("MẮT BIẾC")]
    [InlineData("tinh cam")]
    public async Task Movie_search_ignores_case_and_vietnamese_marks(string keyword)
    {
        await using var db = TestDatabase.Create();
        var movie = db.SeedMovie("Mắt Biếc");
        movie.Genre = "Tình cảm";
        db.SeedMovie("Dune");
        await db.SaveChangesAsync();

        var found = await new MovieCatalogService(db).SearchAsync(new MovieSearchQuery(keyword));

        Assert.Equal("Mắt Biếc", Assert.Single(found).Title);
    }

    [Fact]
    public async Task Vnpay_callback_without_parameters_is_rejected_instead_of_throwing()
    {
        var service = new VnpayPaymentService(Options.Create(new VnpayOptions()), NullLogger<VnpayPaymentService>.Instance);
        var request = new VnpayPaymentRequest("", 0, "", "", "", "secret", "", "", "", 15);

        var result = await service.VerifyVnpayPaymentAsync(request, new Dictionary<string, string?> { ["vnp_TxnRef"] = "CB-1" });

        Assert.False(result.SignatureValid);
    }
}
