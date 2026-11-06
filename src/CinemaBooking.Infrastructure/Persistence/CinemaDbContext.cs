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
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<TheaterPricing> TheaterPricings => Set<TheaterPricing>();
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

        b.Entity<Room>().HasIndex(r => new { r.TheaterId, r.RoomName }).IsUnique();

        b.Entity<Promotion>().HasIndex(p => new { p.TheaterId, p.EndDate });

        b.Entity<Theater>().HasIndex(t => t.Name).IsUnique();
        b.Entity<Theater>().HasIndex(t => t.City);

        // Branch admin gắn với đúng một chi nhánh. Xóa chi nhánh thì tài khoản vẫn còn nhưng mất
        // phạm vi (SetNull) — không xóa luôn tài khoản đăng nhập.
        b.Entity<ApplicationUser>()
            .HasOne(u => u.Theater).WithMany()
            .HasForeignKey(u => u.TheaterId)
            .OnDelete(DeleteBehavior.SetNull);
        b.Entity<ApplicationUser>().HasIndex(u => u.TheaterId);

        b.Entity<Showtime>().Property(s => s.BasePrice).HasPrecision(18, 2);
        b.Entity<Booking>().Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Entity<BookingSeat>().Property(x => x.PriceAtBooking).HasPrecision(18, 2);
        b.Entity<BookingSeat>().Property(x => x.DiscountAmount).HasPrecision(18, 2);
        b.Entity<BookingSeat>().Property(x => x.VoucherShare).HasPrecision(18, 2);
        b.Entity<Promotion>().Property(p => p.FixedPrice).HasPrecision(18, 2);
        b.Entity<Booking>().Property(x => x.VoucherDiscount).HasPrecision(18, 2);

        b.Entity<Voucher>().HasIndex(v => v.Code).IsUnique();
        b.Entity<Voucher>().Property(v => v.DiscountAmount).HasPrecision(18, 2);
        b.Entity<Voucher>().Property(v => v.MaxDiscountAmount).HasPrecision(18, 2);
        b.Entity<Voucher>().Property(v => v.MinOrderAmount).HasPrecision(18, 2);
        b.Entity<Voucher>().HasOne(v => v.Theater).WithMany().HasForeignKey(v => v.TheaterId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<TheaterPricing>().HasIndex(p => p.TheaterId).IsUnique();
        b.Entity<TheaterPricing>().HasOne(p => p.Theater).WithMany().HasForeignKey(p => p.TheaterId).OnDelete(DeleteBehavior.Cascade);
        foreach (var prop in new[] { nameof(TheaterPricing.StandardPrice), nameof(TheaterPricing.ThreeDPrice), nameof(TheaterPricing.ImaxPrice), nameof(TheaterPricing.FourDxPrice), nameof(TheaterPricing.VipSurcharge), nameof(TheaterPricing.CoupleSurcharge), nameof(TheaterPricing.PeakSurcharge), nameof(TheaterPricing.WeekendSurcharge) })
            b.Entity<TheaterPricing>().Property(prop).HasPrecision(18, 2);
        b.Entity<Payment>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<DailyRevenueReport>().Property(x => x.Revenue).HasPrecision(18, 2);

        b.Entity<Booking>().HasIndex(x => x.BookingCode).IsUnique();

        // Chốt chặn cuối chống đặt trùng: dù lock trong RAM có sai, DB vẫn từ chối.
        // Index KHÔNG lọc theo trạng thái đơn (BookingSeats không có cột trạng thái) — đơn hết hạn
        // phải xóa dòng BookingSeats thì ghế mới được đặt lại.
        b.Entity<BookingSeat>()
            .HasIndex(x => new { x.ShowtimeId, x.SeatId })
            .IsUnique();

        // ShowtimeId lặp lại từ Booking chỉ để đặt index ở trên; FK giữ cho nó luôn trỏ vào suất có thật.
        // Restrict: Showtime → Bookings → BookingSeats đã cascade, thêm đường cascade thứ hai
        // là multiple cascade paths trên SQL Server.
        b.Entity<BookingSeat>().HasOne(x => x.Showtime).WithMany().HasForeignKey(x => x.ShowtimeId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Booking>()
            .HasOne(x => x.Payment).WithOne(p => p.Booking!)
            .HasForeignKey<Payment>(p => p.BookingId);

        // Không cascade từ Seat/Showtime xuống BookingSeat để tránh multiple cascade paths trên SQL Server.
        b.Entity<BookingSeat>().HasOne(x => x.Seat).WithMany().HasForeignKey(x => x.SeatId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<OutboxEmail>().HasIndex(x => new { x.Status, x.CreatedUtc });

        // Worker chạy lại cho cùng một ngày không được sinh dòng trùng.
        b.Entity<DailyRevenueReport>()
            .HasIndex(x => new { x.ReportDate, x.TheaterId, x.MovieId })
            .IsUnique();
    }
}
