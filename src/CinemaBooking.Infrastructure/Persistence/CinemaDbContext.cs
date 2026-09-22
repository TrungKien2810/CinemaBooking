using CinemaBooking.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Infrastructure.Persistence;

public class CinemaDbContext : IdentityDbContext<ApplicationUser>
{
    public CinemaDbContext(DbContextOptions<CinemaDbContext> options) : base(options) { }

    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<Theater> Theaters => Set<Theater>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Showtime> Showtimes => Set<Showtime>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingSeat> BookingSeats => Set<BookingSeat>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OutboxEmail> OutboxEmails => Set<OutboxEmail>();
    public DbSet<DailyRevenueReport> DailyRevenueReports => Set<DailyRevenueReport>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Seat>()
            .HasIndex(s => new { s.RoomId, s.RowNumber, s.ColumnNumber })
            .IsUnique();

        b.Entity<Showtime>()
            .HasIndex(s => new { s.RoomId, s.StartTimeUtc });

        b.Entity<Theater>().HasIndex(t => t.Name).IsUnique();
        b.Entity<Theater>().HasIndex(t => t.City);

        b.Entity<Showtime>().Property(s => s.BasePrice).HasPrecision(18, 2);
        b.Entity<Booking>().Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Entity<BookingSeat>().Property(x => x.PriceAtBooking).HasPrecision(18, 2);
        b.Entity<Payment>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<DailyRevenueReport>().Property(x => x.Revenue).HasPrecision(18, 2);

        b.Entity<Booking>().HasIndex(x => x.BookingCode).IsUnique();

        // Chốt chặn cuối chống đặt trùng: dù lock trong RAM có sai, DB vẫn từ chối.
        // Filtered index để vé đã hủy không chiếm chỗ của ghế.
        b.Entity<BookingSeat>()
            .HasIndex(x => new { x.ShowtimeId, x.SeatId })
            .IsUnique();

        b.Entity<Booking>()
            .HasOne(x => x.Payment).WithOne(p => p.Booking!)
            .HasForeignKey<Payment>(p => p.BookingId);

        // Không cascade từ Seat/Showtime xuống BookingSeat để tránh multiple cascade paths trên SQL Server.
        b.Entity<BookingSeat>().HasOne(x => x.Seat).WithMany().HasForeignKey(x => x.SeatId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<OutboxEmail>().HasIndex(x => new { x.Status, x.CreatedUtc });
    }
}
