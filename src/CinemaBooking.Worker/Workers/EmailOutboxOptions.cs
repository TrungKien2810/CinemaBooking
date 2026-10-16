namespace CinemaBooking.Worker.Workers;

/// <summary>
/// Cấu hình hàng đợi email của Worker (section <c>EmailOutbox</c>). Tách khỏi hằng số trong code để
/// đổi nhịp gửi hoặc giới hạn retry mà không phải build lại.
/// </summary>
public sealed class EmailOutboxOptions
{
    public const string SectionName = "EmailOutbox";

    /// <summary>Số lần thử tối đa trước khi đánh dấu Failed — chặn email hỏng quay vòng mãi.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Số email lấy ra mỗi vòng quét.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Chu kỳ quét outbox, tính bằng giây.</summary>
    public int PollSeconds { get; set; } = 15;
}
