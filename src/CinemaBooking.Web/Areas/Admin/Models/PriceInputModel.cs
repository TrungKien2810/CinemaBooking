namespace CinemaBooking.Web.Areas.Admin.Models;

/// <summary>Dữ liệu cho ô nhập giá theo nghìn đồng (<c>_PriceInput</c>).</summary>
public sealed record PriceInputModel(string Name, string Label, decimal? Value, string? Hint = null);
