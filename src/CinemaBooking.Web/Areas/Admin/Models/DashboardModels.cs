namespace CinemaBooking.Web.Areas.Admin.Models;

public sealed record DashboardShowtimeRow(
    int Id, string MovieTitle, string TheaterName, string RoomName, DateTime StartLocal,
    int Sold, int Capacity, bool IsActive)
{
    public double Occupancy => Capacity == 0 ? 0 : (double)Sold / Capacity;
}

public sealed record DashboardViewModel(
    string Scope, DateOnly Today, int ShowtimesToday, int TicketsToday, decimal RevenueToday,
    int Tickets7Days, decimal Revenue7Days, double OccupancyToday, List<DashboardShowtimeRow> Upcoming);
