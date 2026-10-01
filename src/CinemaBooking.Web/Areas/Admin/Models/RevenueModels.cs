using Microsoft.AspNetCore.Mvc.Rendering;

namespace CinemaBooking.Web.Areas.Admin.Models;

public sealed record RevenueByDayRow(DateOnly Day, int TheaterId, string TheaterName, int Tickets, decimal Revenue);

public sealed record RevenueByTheaterRow(int TheaterId, string TheaterName, int Tickets, decimal Revenue);

public sealed record RevenueDashboardViewModel(
    DateOnly From, DateOnly To, List<RevenueByDayRow> ByDay, List<RevenueByTheaterRow> ByTheater)
{
    public int TotalTickets => ByTheater.Sum(t => t.Tickets);
    public decimal TotalRevenue => ByTheater.Sum(t => t.Revenue);
}

public sealed record RevenueByMovieRow(int MovieId, string MovieTitle, int Tickets, decimal Revenue);

public sealed record RevenueByShowtimeRow(
    int ShowtimeId, string MovieTitle, string TheaterName, string RoomName, DateTime StartLocal,
    int Tickets, int Capacity, decimal Revenue)
{
    /// <summary>Tỉ lệ lấp đầy phòng của suất, 0–1.</summary>
    public double Occupancy => Capacity == 0 ? 0 : (double)Tickets / Capacity;
}

public sealed record RevenueBreakdownViewModel(
    DateOnly From, DateOnly To, int? TheaterId, List<SelectListItem> Theaters,
    List<RevenueByMovieRow> ByMovie, List<RevenueByShowtimeRow> ByShowtime);
