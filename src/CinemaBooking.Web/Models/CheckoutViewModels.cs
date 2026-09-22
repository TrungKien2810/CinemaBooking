using CinemaBooking.Domain.Enums;

namespace CinemaBooking.Web.Models;

/// <summary>Tóm tắt giỏ ghế đang lưu trong Session trước khi khách điền thông tin và thanh toán.</summary>
public sealed record CheckoutViewModel(
    int ShowtimeId,
    string MovieTitle,
    string TheaterName,
    string RoomName,
    DateTime StartLocal,
    IReadOnlyList<CheckoutSeatItem> Seats);

public sealed record CheckoutSeatItem(string Label, SeatType Type);
