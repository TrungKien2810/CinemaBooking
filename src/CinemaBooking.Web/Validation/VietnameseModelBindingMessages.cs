using Microsoft.AspNetCore.Mvc;

namespace CinemaBooking.Web.Validation;

/// <summary>
/// Việt hóa thông báo lỗi do chính MVC sinh ra (không nằm trong attribute nào của view model):
/// gõ "abc" vào ô số, bỏ trống ô int non-nullable… Mặc định chúng là tiếng Anh, và
/// <c>ValueMustBeANumber</c> còn được render thành <c>data-val-number</c> cho jQuery Validate —
/// nên sửa ở đây là sửa luôn cả thông báo phía client.
/// </summary>
public static class VietnameseModelBindingMessages
{
    public static void Apply(MvcOptions options)
    {
        var p = options.ModelBindingMessageProvider;

        p.SetValueMustBeANumberAccessor(field => $"{field} phải là số.");
        p.SetAttemptedValueIsInvalidAccessor((value, field) => $"Giá trị “{value}” không hợp lệ cho {field}.");
        p.SetValueIsInvalidAccessor(value => $"Giá trị “{value}” không hợp lệ.");
        p.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"Giá trị “{value}” không hợp lệ.");
        p.SetUnknownValueIsInvalidAccessor(field => $"Giá trị của {field} không hợp lệ.");
        p.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Giá trị không hợp lệ.");
        p.SetValueMustNotBeNullAccessor(value => $"Giá trị “{value}” không được để trống.");
        p.SetMissingBindRequiredValueAccessor(field => $"Thiếu giá trị cho {field}.");
        p.SetMissingKeyOrValueAccessor(() => "Thiếu giá trị.");
        p.SetMissingRequestBodyRequiredValueAccessor(() => "Thiếu dữ liệu gửi lên.");
        p.SetNonPropertyValueMustBeANumberAccessor(() => "Giá trị phải là số.");
    }
}
