using CinemaBooking.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CinemaBooking.Web.Areas.Admin.Models;

public sealed record BookingAdminItem(
    int Id, string BookingCode, string CustomerName, string MovieTitle, string TheaterName,
    DateTime ShowtimeStartLocal, DateTime BookedLocal, int SeatCount, decimal TotalAmount, BookingStatus Status);

public sealed record BookingsIndexViewModel(
    string? Query, int? TheaterId, BookingStatus? Status,
    List<SelectListItem> Theaters, List<BookingAdminItem> Items, int PageSize,
    DateOnly? From = null, DateOnly? To = null, CinemaBooking.Web.Models.PageInfo? Paging = null)
{
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Query) || TheaterId is not null || Status is not null || From is not null || To is not null;
}

public sealed record BookingSeatLine(string Label, SeatType SeatType, decimal Price);

public sealed record BookingDetailsViewModel(
    int Id, string BookingCode, BookingStatus Status, DateTime BookedLocal,
    string CustomerName, string CustomerEmail, string? CustomerPhone,
    string MovieTitle, string TheaterName, string RoomName, DateTime ShowtimeStartLocal,
    decimal TotalAmount, PaymentStatus? PaymentStatus, string? TransactionId,
    List<BookingSeatLine> Seats, string? VoucherCode = null, decimal VoucherDiscount = 0m);
