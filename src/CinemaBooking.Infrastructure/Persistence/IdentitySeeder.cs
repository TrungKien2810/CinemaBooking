using CinemaBooking.Domain;
using CinemaBooking.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CinemaBooking.Infrastructure.Persistence;

/// <summary>
/// Dữ liệu mẫu cho tài khoản và vai trò.
/// Tách riêng khỏi CatalogSeeder (phim, rạp, phòng, suất chiếu — Dev A) để hai người
/// sửa hai file khác nhau, không đụng nhau lúc merge.
/// </summary>
public static class IdentitySeeder
{
    private const string DefaultAdminEmail = "admin@cinemabooking.local";
    private const string DefaultAdminPassword = "Admin@12345";

    private const string DefaultBranchAdminEmail = "branch@cinemabooking.local";
    private const string DefaultBranchAdminPassword = "Branch@12345";
    private const string DefaultBranchTheaterName = "CineSmart Cầu Giấy";

    /// <summary>
    /// Tạo vai trò Admin/Customer và một tài khoản admin nếu chưa có.
    /// Chạy được nhiều lần — mỗi bước đều kiểm tra tồn tại trước khi tạo.
    /// </summary>
    /// <param name="sp">Service provider của MỘT scope, không phải root provider:
    /// RoleManager và UserManager đều là Scoped.</param>
    public static async Task SeedAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));

        await EnsureDatabaseReadyAsync(sp, ct);
        await SeedRolesAsync(sp, logger);
        await SeedAdminAsync(sp, logger);
        await SeedBranchAdminAsync(sp, logger);
    }

    /// <summary>
    /// Seed chạy lúc khởi động nên nếu database chưa có, lỗi bật ra sẽ là
    /// "Invalid object name 'AspNetRoles'" — chẳng gợi ý được gì. Đổi thành thông báo
    /// chỉ thẳng câu lệnh cần chạy.
    /// </summary>
    private static async Task EnsureDatabaseReadyAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<CinemaDbContext>();

        var ready = await db.Database.CanConnectAsync(ct)
                    && (await db.Database.GetAppliedMigrationsAsync(ct)).Any();

        if (!ready)
        {
            throw new InvalidOperationException(
                "Database chưa sẵn sàng: chưa kết nối được hoặc chưa có migration nào được áp dụng. " +
                "Chạy: dotnet ef database update -p src/CinemaBooking.Infrastructure -s src/CinemaBooking.Web");
        }
    }

    private static async Task SeedRolesAsync(IServiceProvider sp, ILogger logger)
    {
        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in AppRoles.All)
        {
            if (await roles.RoleExistsAsync(role)) continue;

            var result = await roles.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded) throw Failed($"tạo vai trò {role}", result);

            logger.LogInformation("Đã tạo vai trò {Role}.", role);
        }
    }

    private static async Task SeedAdminAsync(IServiceProvider sp, ILogger logger)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        // Không dùng ?? : khóa config để rỗng ("SeedAdmin:Password": "") vẫn khác null,
        // sẽ lọt xuống CreateAsync rồi mới hỏng với lỗi mật khẩu khó hiểu.
        var email = Setting(config, "SeedAdmin:Email", DefaultAdminEmail);
        var password = Setting(config, "SeedAdmin:Password", DefaultAdminPassword);

        var admin = await users.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = Setting(config, "SeedAdmin:FullName", "Quản trị viên"),
            };

            var result = await users.CreateAsync(admin, password);
            if (!result.Succeeded) throw Failed($"tạo tài khoản admin {email}", result);

            logger.LogInformation("Đã tạo tài khoản admin {Email}.", email);
        }

        // Tách khỏi nhánh trên: nếu ai đó lỡ gỡ admin khỏi vai trò, lần khởi động sau gắn lại.
        if (!await users.IsInRoleAsync(admin, AppRoles.Admin))
        {
            var result = await users.AddToRoleAsync(admin, AppRoles.Admin);
            if (!result.Succeeded) throw Failed($"gán vai trò Admin cho {email}", result);
        }

        if (password == DefaultAdminPassword)
        {
            logger.LogWarning(
                "Tài khoản admin đang dùng mật khẩu mặc định trong mã nguồn. " +
                "Đặt SeedAdmin:Password trong appsettings.Development.json hoặc user-secrets.");
        }
    }

    /// <summary>
    /// Tạo tài khoản quản lý chi nhánh (BranchAdmin) gắn với một chi nhánh có thật.
    /// CatalogSeeder chạy TRƯỚC seeder này nên chi nhánh đã tồn tại; nếu đổi tên chi nhánh trong
    /// cấu hình thì bỏ qua kèm cảnh báo, không làm sập khởi động.
    /// </summary>
    private static async Task SeedBranchAdminAsync(IServiceProvider sp, ILogger logger)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<CinemaDbContext>();

        var theaterName = Setting(config, "SeedBranchAdmin:TheaterName", DefaultBranchTheaterName);
        var theater = await db.Theaters.FirstOrDefaultAsync(t => t.Name == theaterName);
        if (theater is null)
        {
            logger.LogWarning(
                "Không tìm thấy chi nhánh '{Theater}' để gán cho tài khoản quản lý chi nhánh — bỏ qua.",
                theaterName);
            return;
        }

        var email = Setting(config, "SeedBranchAdmin:Email", DefaultBranchAdminEmail);
        var password = Setting(config, "SeedBranchAdmin:Password", DefaultBranchAdminPassword);

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = Setting(config, "SeedBranchAdmin:FullName", "Quản lý chi nhánh"),
                TheaterId = theater.Id,
            };

            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded) throw Failed($"tạo tài khoản quản lý chi nhánh {email}", created);

            logger.LogInformation("Đã tạo tài khoản quản lý chi nhánh {Email} cho {Theater}.", email, theater.Name);
        }
        else if (user.TheaterId != theater.Id)
        {
            // Đổi cấu hình chi nhánh thì lần khởi động sau tự cập nhật lại phạm vi.
            user.TheaterId = theater.Id;
            var updated = await users.UpdateAsync(user);
            if (!updated.Succeeded) throw Failed($"gán chi nhánh cho {email}", updated);
        }

        if (!await users.IsInRoleAsync(user, AppRoles.BranchAdmin))
        {
            var result = await users.AddToRoleAsync(user, AppRoles.BranchAdmin);
            if (!result.Succeeded) throw Failed($"gán vai trò BranchAdmin cho {email}", result);
        }

        if (password == DefaultBranchAdminPassword)
        {
            logger.LogWarning(
                "Tài khoản quản lý chi nhánh đang dùng mật khẩu mặc định trong mã nguồn. " +
                "Đặt SeedBranchAdmin:Password trong appsettings.Development.json hoặc user-secrets.");
        }
    }

    private static string Setting(IConfiguration config, string key, string fallback)
    {
        var value = config[key];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static InvalidOperationException Failed(string what, IdentityResult result) =>
        new($"Seed thất bại khi {what}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
}
