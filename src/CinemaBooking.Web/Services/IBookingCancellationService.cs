using CinemaBooking.Domain.Entities;

namespace CinemaBooking.Web.Services;

public enum CancelBookingOutcome
{
    Success,
    NotFound,
    NotOwner,
    NotCancellable,
    TooLate,
    AlreadyCancelled,
    RefundFailed,
}

public sealed record CancelBookingRequest(
    string BookingCode,
    string? UserId,
    string? Email,
    bool IsAdmin,
    string Actor);

public sealed record CancelBookingResult(
    CancelBookingOutcome Outcome,
    string Message,
    decimal RefundedAmount = 0,
    int ShowtimeId = 0,
    IReadOnlyList<int>? SeatIds = null)
{
    public bool IsSuccess => Outcome == CancelBookingOutcome.Success;
}

/// <summary>
/// Hủy vé phía khách (và admin dùng lại): kiểm tra chủ vé, hạn hủy, hoàn tiền VNPAY,
/// trả ghế, ghi email vào outbox và trả danh sách ghế để caller phát SignalR.
/// </summary>
public interface IBookingCancellationService
{
    Task<CancelBookingResult> CancelAsync(CancelBookingRequest request, CancellationToken ct = default);

    /// <summary>Kiểm tra nhanh để view hiện/ẩn nút hủy mà không cần gọi API hoàn tiền.</summary>
    Task<CancelEligibility> GetEligibilityAsync(Booking booking, CancellationToken ct = default);
}

public sealed record CancelEligibility(bool CanCancel, string? Reason, decimal RefundAmount);
