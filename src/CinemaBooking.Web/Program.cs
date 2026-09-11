using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Hubs;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

// DbContext + SeatLockManager + PaymentService + EmailSender
builder.Services.AddCinemaInfrastructure(builder.Configuration);

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedAccount = false;

        // Khóa tài khoản 5 phút sau 5 lần sai mật khẩu — chặn dò mật khẩu.
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<CinemaDbContext>()
    .AddDefaultTokenProviders();

// ── Dev C: cookie đăng nhập trỏ về AccountController (MVC), không dùng Razor Pages Identity UI ──
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.ReturnUrlParameter = "returnUrl";
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
    o.SlidingExpiration = true;
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
});

// Giỏ ghế tạm giữa trang chọn ghế và trang thanh toán.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromMinutes(20);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
});

var app = builder.Build();

// ── Dev C: vai trò và tài khoản admin ──
// Phải có sẵn trước request đầu tiên, nếu không trang admin khóa cứng chính mình.
// Scope riêng vì RoleManager/UserManager là Scoped.
// Lưu ý khi merge: khối này phải chạy SAU bước áp migration của Dev A.
using (var scope = app.Services.CreateScope())
{
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<SeatHub>("/hubs/seat");

app.Run();
