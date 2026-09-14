using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Logging;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Hubs;
using CinemaBooking.Web.Middleware;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

// ── Dev B: log có cấu trúc, dùng chung cấu hình với Worker ──
builder.Logging.AddCinemaLogging(builder.Environment.IsDevelopment());

// DbContext + SeatLockManager + PaymentService + EmailSender
builder.Services.AddCinemaInfrastructure(builder.Configuration);

// ── Dev B: truy vấn catalog & lịch chiếu ──
builder.Services.Configure<DisplayTimeZone>(
    builder.Configuration.GetSection(DisplayTimeZone.SectionName));
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddScoped<IShowtimeQueryService, ShowtimeQueryService>();
builder.Services.AddScoped<IMovieCatalogService, MovieCatalogService>();
builder.Services.AddScoped<IShowtimeScheduler, ShowtimeScheduler>();

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<CinemaDbContext>()
    .AddDefaultTokenProviders();

// Giỏ ghế tạm giữa trang chọn ghế và trang thanh toán.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromMinutes(20);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
});

var app = builder.Build();

// Đặt đầu pipeline: middleware chỉ bắt được exception của những middleware nằm sau nó.
app.UseCinemaExceptionHandling();

// --- Dev A: khởi tạo dữ liệu ---
// Áp migration rồi seed catalog ngay lúc khởi động: cả nhóm chỉ cần `git pull` + `dotnet run`
// là có schema và dữ liệu mẫu giống nhau, không phải nhớ chạy `dotnet ef database update`.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();
    await db.Database.MigrateAsync();
    await CatalogSeeder.SeedAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.UseCinemaRequestLogScope();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<SeatHub>("/hubs/seat");

app.Run();
