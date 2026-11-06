using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Web.Models;

namespace CinemaBooking.Web.Services;

/// <summary>Dựng <see cref="BookingTicketViewModel"/> từ đơn; một nơi quyết định có che thông tin cá nhân hay không.</summary>
public static class BookingTicketMapper
{
    /// <param name="revealPersonalData">
    /// true cho chủ vé / nhân viên rạp; false cho người chỉ cầm mã vé — họ tên, email, SĐT bị che.
    /// </param>
    public static BookingTicketViewModel ToViewModel(Booking booking, DisplayTimeZone tz, bool revealPersonalData)
    {
        var showtime = booking.Showtime!;

        var seats = booking.Seats
            .Where(bs => bs.Seat is not null)
            .OrderBy(bs => bs.Seat!.RowNumber).ThenBy(bs => bs.Seat!.ColumnNumber)
            .Select(bs => new BookingSeatLine(
                bs.Seat!.Label, bs.Seat.SeatType, bs.PriceAtBooking, bs.DiscountAmount, bs.PromotionTitle, bs.VoucherShare))
            .ToList();

        return new BookingTicketViewModel(
            booking.BookingCode,
            showtime.Movie?.Title ?? string.Empty,
            showtime.Room?.Theater?.Name ?? string.Empty,
            showtime.Room?.Theater?.City ?? string.Empty,
            showtime.Room?.RoomName ?? string.Empty,
            tz.ToLocalTime(showtime.StartTimeUtc),
            tz.ToLocalTime(booking.BookingTimeUtc),
            booking.Status,
            booking.Payment?.Status ?? PaymentStatus.Pending,
            booking.Payment?.PaymentMethod ?? "VNPAY",
            booking.Payment?.TransactionId,
            revealPersonalData ? booking.CustomerName : PersonalDataMask.Name(booking.CustomerName),
            revealPersonalData ? booking.CustomerEmail : PersonalDataMask.Email(booking.CustomerEmail),
            revealPersonalData ? booking.CustomerPhone : PersonalDataMask.Phone(booking.CustomerPhone),
            booking.TotalAmount,
            seats,
            IsMasked: !revealPersonalData,
            CheckedInLocal: booking.CheckedInUtc is { } at ? tz.ToLocalTime(at) : null,
            CheckedInBy: revealPersonalData ? booking.CheckedInBy : null,
            FormatLabel: showtime.Room is { } room ? room.RoomType.Label() + " · " + showtime.Language.Label() : null,
            VoucherCode: booking.VoucherCode,
            VoucherDiscount: booking.VoucherDiscount);
    }
}
