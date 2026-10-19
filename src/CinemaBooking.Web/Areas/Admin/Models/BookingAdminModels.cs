using CinemaBooking.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CinemaBooking.Web.Areas.Admin.Models;

public sealed record BookingAdminItem(
    int Id, string BookingCode, string CustomerName, string MovieTitle, string TheaterName,
    DateTime ShowtimeStartLocal, DateTime BookedLocal, int SeatCount, decimal TotalAmount, BookingStatus Status);

public sealed record BookingsIndexViewModel(
    string? Code, int? TheaterId, BookingStatus? Status,
    List<SelectListItem> Theaters, List<BookingAdminItem> Items, int MaxItems);

public sealed record BookingSeatLine(string Label, SeatType SeatType, decimal Price);

public sealed record BookingDetailsViewModel(
    int Id, string BookingCode, BookingStatus Status, DateTime BookedLocal,
    string CustomerName, string CustomerEmail, string? CustomerPhone,
    string MovieTitle, string TheaterName, string RoomName, DateTime ShowtimeStartLocal,
    decimal TotalAmount, PaymentStatus? PaymentStatus, string? TransactionId,
    List<BookingSeatLine> Seats)
{
    /// <summary>Chỉ đơn còn giữ ghế mới hủy được — đơn đã hủy/hết hạn đã trả ghế rồi.</summary>
    public bool CanCancel => Status is BookingStatus.Pending or BookingStatus.Confirmed;
}
