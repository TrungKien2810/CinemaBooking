using CinemaBooking.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CinemaBooking.Web.Areas.Admin.Models;

public sealed record BookingAdminItem(
    int Id, string BookingCode, string CustomerName, string MovieTitle, string TheaterName,
    DateTime ShowtimeStartLocal, DateTime BookedLocal, int SeatCount, decimal TotalAmount, BookingStatus Status);

public sealed record BookingsIndexViewModel(
    string? Code, int? TheaterId, BookingStatus? Status,
    List<SelectListItem> Theaters, List<BookingAdminItem> Items, int MaxItems);
