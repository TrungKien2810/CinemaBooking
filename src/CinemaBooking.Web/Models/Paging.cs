namespace CinemaBooking.Web.Models;

/// <summary>Thông tin phân trang cho các danh sách dài (đơn đặt vé, tài khoản...).</summary>
public sealed record PageInfo(int Page, int PageSize, int Total)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public int From => Total == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int To => Math.Min(Total, Page * PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    /// <summary>Số trang hợp lệ từ giá trị gõ trên URL (âm, 0, quá lớn đều được kẹp lại).</summary>
    public static int Clamp(int? page, int total, int pageSize) =>
        Math.Clamp(page ?? 1, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
}

/// <summary>Dữ liệu cho <c>_Pager</c>: action cần link tới, thông tin trang và các tham số lọc giữ lại trên URL.</summary>
public sealed record PagerModel(string Action, PageInfo Info, IDictionary<string, object?> Route);
