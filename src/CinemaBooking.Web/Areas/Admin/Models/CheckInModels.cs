using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;

namespace CinemaBooking.Web.Areas.Admin.Models;

/// <summary>
/// Kết quả tra mã vé ở quầy soát vé. <see cref="OtherBranch"/>: vé của chi nhánh khác — chỉ biết tên chi nhánh,
/// không lộ thông tin khách cho BranchAdmin không thuộc chi nhánh đó.
/// </summary>
public sealed record CheckInPageModel(
    string? Code,
    CheckInOutcome? Outcome,
    string? OtherBranch,
    BookingTicketViewModel? Ticket,
    DateTime? OpensAtLocal)
{
    public bool CanCheckIn => Outcome == CheckInOutcome.Ok && OtherBranch is null && Ticket is not null;
}
