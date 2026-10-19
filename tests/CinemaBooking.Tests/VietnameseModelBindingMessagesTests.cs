using CinemaBooking.Web.Validation;
using Microsoft.AspNetCore.Mvc;

namespace CinemaBooking.Tests;

public class VietnameseModelBindingMessagesTests
{
    [Fact]
    public void Apply_replaces_the_english_defaults_used_by_forms()
    {
        var options = new MvcOptions();
        var english = options.ModelBindingMessageProvider.ValueMustBeANumberAccessor("Giá vé");

        VietnameseModelBindingMessages.Apply(options);
        var p = options.ModelBindingMessageProvider;

        Assert.StartsWith("The field", english);
        // Chính chuỗi này được render thành data-val-number cho jQuery Validate.
        Assert.Equal("Giá vé phải là số.", p.ValueMustBeANumberAccessor("Giá vé"));
        Assert.Equal("Giá trị “abc” không hợp lệ cho Thời lượng.", p.AttemptedValueIsInvalidAccessor("abc", "Thời lượng"));
    }
}
