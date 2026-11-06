using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure;
using CinemaBooking.Infrastructure.Logging;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Hubs;
using CinemaBooking.Web.Middleware;
using CinemaBooking.Web.Services;
using CinemaBooking.Web.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

// ── Dev B: log có cấu trúc, dùng chung cấu hình với Worker ──
builder.Logging.AddCinemaLogging(builder.Environment.IsDevelopment());

// Mô phỏng thanh toán khi chưa có VNPAY HashSecret CHỈ bật ở môi trường Development và chỉ khi chưa ai
// đặt giá trị rõ ràng (appsettings.Development.json / biến môi trường vẫn ghi đè được). Không để cờ này
// trong appsettings.json dùng chung: chạy thật mà quên cấu hình VNPAY thì sẽ bán vé miễn phí.
if (builder.Environment.IsDevelopment() && builder.Configuration["Vnpay:SimulateWhenUnconfigured"] is null)
    builder.Configuration["Vnpay:SimulateWhenUnconfigured"] = "true";

// DbContext + SeatLockManager + PaymentService + EmailSender
builder.Services.AddCinemaInfrastructure(builder.Configuration);

// ── Dev B: truy vấn catalog & lịch chiếu ──
builder.Services.Configure<DisplayTimeZone>(
    builder.Configuration.GetSection(DisplayTimeZone.SectionName));
// BookingService nhận trực tiếp DisplayTimeZone (không qua IOptions)
// để không phải inject IOptions ở mọi nơi.
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DisplayTimeZone>>().Value);
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddScoped<IShowtimeQueryService, ShowtimeQueryService>();
builder.Services.AddScoped<IMovieCatalogService, MovieCatalogService>();
builder.Services.AddScoped<IShowtimeScheduler, ShowtimeScheduler>();

// ── Dev A: sơ đồ ghế & đặt vé ──
builder.Services.AddScoped<IBookedSeatQuery, BookedSeatQuery>();
builder.Services.AddSingleton<ISeatPricingService, SeatPricingService>();

// ── Dev B: SeatHub kiểm tra ghế ở DB trước khi cấp lock ──
builder.Services.AddScoped<ISeatAvailabilityQuery, SeatAvailabilityQuery>();

// ── Dọn ghế phải chạy TRONG process Web: bảng lock là in-memory singleton của process này, và chỉ ở
//    đây mới có IHubContext để báo SeatUnlocked cho các tab đang mở suất chiếu. ──
builder.Services.AddHostedService<SeatLockCleaner>();       // lock RAM hết hạn (mỗi 10s)
builder.Services.AddHostedService<ExpiredBookingCleaner>(); // đơn Pending quá hạn thanh toán (mỗi 30s)

// ── Dev B: luồng đặt vé (kiểm ghế + lock → ghi Booking trong một transaction → thu tiền → outbox) ──
builder.Services.AddScoped<IBookingService, BookingService>();

// ── Vé điện tử: mã QR in trên vé ──
builder.Services.AddSingleton<IQrCodeService, QrCodeService>();

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

// ── Dev C: danh bạ chi nhánh cho bộ lọc thành phố và trang chi nhánh ──
builder.Services.AddScoped<IBranchDirectory, BranchDirectory>();

// ── Phạm vi dữ liệu của tài khoản quản lý chi nhánh (BranchAdmin) ──
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IBranchScope, BranchScope>();
builder.Services.AddScoped<ITicketCheckInService, TicketCheckInService>();
// ── Dev C: thông báo lỗi model binding bằng tiếng Việt (cả server lẫn data-val-number) ──
builder.Services.Configure<MvcOptions>(VietnameseModelBindingMessages.Apply);

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

// --- Dev A + Dev C: khởi tạo dữ liệu (thứ tự bắt buộc: migration → catalog → identity) ---
// Áp migration rồi seed catalog ngay lúc khởi động: cả nhóm chỉ cần `git pull` + `dotnet run`
// là có schema và dữ liệu mẫu giống nhau, không phải nhớ chạy `dotnet ef database update`.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();
    await db.Database.MigrateAsync();
    await CatalogSeeder.SeedAsync(db);
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);

    // Dữ liệu demo phong phú (chi nhánh, phim, khách, lịch chiếu ~4 tuần, vài nghìn đơn đặt vé). Mặc định chỉ bật ở
    // Development; đặt Seed:DemoData=false để tắt, hoặc =true để bật ở môi trường khác.
    if (builder.Configuration.GetValue<bool?>("Seed:DemoData") ?? app.Environment.IsDevelopment())
        await DemoDataSeeder.SeedAsync(scope.ServiceProvider);
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

// --- Dev A: khu quản trị (/Admin/...) — phải map trước route default ---
app.MapControllerRoute(
    name: "admin",
    pattern: "{area:exists}/{controller=Movies}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<SeatHub>("/hubs/seat");

app.Run();
