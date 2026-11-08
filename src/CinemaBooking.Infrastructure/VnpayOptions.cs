namespace CinemaBooking.Infrastructure;

/// <summary>
/// Cấu hình cổng thanh toán VNPAY (môi trường Sandbox khi <c>Environment=Development</c>).
/// Section <c>Vnpay</c> trong appsettings. Hai process (Web) phải đọc cùng một cấu hình —
/// hard-code mỗi nơi một kiểu thì checksum giữa merchant và gateway sẽ lệch.
/// </summary>
public sealed class VnpayOptions
{
    public const string SectionName = "Vnpay";

    /// <summary>Mã TmnCode của merchant tại VNPAY (vnp_TmnCode).</summary>
    public string TmnCode { get; set; } = string.Empty;

    /// <summary>Chuỗi bí mật tạo checksum (vnp_HashSecret).</summary>
    public string HashSecret { get; set; } = string.Empty;

    /// <summary>URL cổng thanh toán VNPAY (vnp_Url).</summary>
    public string PaymentUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";

    /// <summary>URL nhận kết quả sau khi khách thanh toán xong (vnp_ReturnUrl).</summary>
    public string ReturnUrl { get; set; } = "http://localhost:5005/Booking/PaymentReturn";

    /// <summary>
    /// Khi <see cref="HashSecret"/> chưa được cấu hình (máy dev/demo chưa có merchant secret), coi
    /// như thanh toán thành công để luồng đặt vé chạy trọn vẹn thay vì chặn khách ở bước cuối.
    /// Mặc định TẮT trong code; chỉ bật ở appsettings của môi trường demo. TUYỆT ĐỐI không bật
    /// ở production — đơn sẽ được xác nhận mà không có đồng tiền nào được thu.
    /// </summary>
    public bool SimulateWhenUnconfigured { get; set; }

    public string Locale { get; set; } = "vn";

    public string OrderType { get; set; } = "other";

    /// <summary>
    /// Số phút VNPAY cho khách thanh toán kể từ lúc tạo đơn (<c>vnp_ExpireDate</c> = CreateDate + giá trị này),
    /// quá hạn VNPAY tự từ chối. Cũng là thời gian ghế bị chặn sau khi khách bấm "Thanh toán".
    /// </summary>
    public int ExpireMinutes { get; set; } = 10;

    public string IpAddr { get; set; } = "127.0.0.1";

    /// <summary>
    /// Thời gian tối đa một đơn <c>Pending</c> được phép giữ ghế trong DB: thời gian VNPAY cho thanh
    /// toán cộng 2 phút gia hạn (đệm cho độ trễ Return/IPN). Quá hạn này (khách đóng tab / thanh toán lỗi không quay về) thì đơn
    /// phải trả ghế — nếu không ghế sẽ bị khóa vĩnh viễn dù chưa ai trả tiền.
    /// </summary>
    public TimeSpan PendingHoldWindow => TimeSpan.FromMinutes(ExpireMinutes + 2);
}
