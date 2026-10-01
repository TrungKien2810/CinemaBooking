namespace CinemaBooking.Web.Areas.Admin.Models;

public sealed record RevenueByDayRow(DateOnly Day, int TheaterId, string TheaterName, int Tickets, decimal Revenue);

public sealed record RevenueByTheaterRow(int TheaterId, string TheaterName, int Tickets, decimal Revenue);

public sealed record RevenueDashboardViewModel(
    DateOnly From, DateOnly To, List<RevenueByDayRow> ByDay, List<RevenueByTheaterRow> ByTheater)
{
    public int TotalTickets => ByTheater.Sum(t => t.Tickets);
    public decimal TotalRevenue => ByTheater.Sum(t => t.Revenue);
}
