using System.Reflection;
using System.Security.Claims;
using CinemaBooking.Domain;
using CinemaBooking.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace CinemaBooking.Tests;

/// <summary>
/// Khách gõ thẳng URL khu quản trị phải bị chặn (General.md §7.7). Test đánh giá đúng policy mà
/// MVC dựng từ các [Authorize] trên controller, với ba loại người dùng: ẩn danh, Customer, Admin.
/// </summary>
public class AdminAuthorizationTests
{
    private static readonly Type[] AdminControllers = typeof(AdminControllerBase).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false }
                    && typeof(Controller).IsAssignableFrom(t)
                    && t.GetCustomAttribute<AreaAttribute>(inherit: true)?.RouteValue == "Admin")
        .ToArray();

    public static TheoryData<Type> AdminControllerTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var t in AdminControllers) data.Add(t);
        return data;
    }

    [Fact]
    public void Admin_area_has_the_expected_controllers()
    {
        // Chống test "xanh vì không quét được gì": nếu đổi namespace/area mà quên, test này đỏ.
        Assert.Contains(typeof(MoviesController), AdminControllers);
        Assert.Contains(typeof(TheatersController), AdminControllers);
        Assert.Contains(typeof(RoomsController), AdminControllers);
        Assert.Contains(typeof(ShowtimesController), AdminControllers);
        Assert.Contains(typeof(BookingsController), AdminControllers);
        Assert.Contains(typeof(RevenueController), AdminControllers);
    }

    [Theory]
    [MemberData(nameof(AdminControllerTypes))]
    public void No_admin_action_opts_out_with_AllowAnonymous(Type controller)
    {
        Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true));

        var anonymousActions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(m => m.Name);
        Assert.Empty(anonymousActions);
    }

    [Theory]
    [MemberData(nameof(AdminControllerTypes))]
    public async Task Anonymous_visitor_is_rejected(Type controller)
    {
        Assert.False(await IsAuthorizedAsync(controller, new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Theory]
    [MemberData(nameof(AdminControllerTypes))]
    public async Task Customer_account_is_rejected(Type controller)
    {
        Assert.False(await IsAuthorizedAsync(controller, SignedIn(AppRoles.Customer)));
    }

    [Theory]
    [MemberData(nameof(AdminControllerTypes))]
    public async Task Admin_account_is_allowed(Type controller)
    {
        Assert.True(await IsAuthorizedAsync(controller, SignedIn(AppRoles.Admin)));
    }

    /// <summary>
    /// BranchAdmin chỉ được vào phòng/ghế và suất chiếu; phim, chi nhánh, đơn, doanh thu là của
    /// Admin toàn hệ thống.
    /// </summary>
    [Theory]
    [MemberData(nameof(AdminControllerTypes))]
    public async Task Branch_admin_is_allowed_only_on_rooms_and_showtimes(Type controller)
    {
        var expected = controller == typeof(RoomsController) || controller == typeof(ShowtimesController);
        Assert.Equal(expected, await IsAuthorizedAsync(controller, SignedIn(AppRoles.BranchAdmin)));
    }

    [Fact]
    public void Branch_admin_role_is_seeded()
    {
        Assert.Contains(AppRoles.BranchAdmin, AppRoles.All);
    }

    private static ClaimsPrincipal SignedIn(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "user@example.com"), new Claim(ClaimTypes.Role, role)], "Test"));

    /// <summary>Gộp mọi [Authorize] (kể cả kế thừa từ AdminControllerBase) thành một policy như MVC làm.</summary>
    private static async Task<bool> IsAuthorizedAsync(Type controller, ClaimsPrincipal user)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var authorizeData = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
        Assert.NotEmpty(authorizeData);

        var policy = await AuthorizationPolicy.CombineAsync(
            services.GetRequiredService<IAuthorizationPolicyProvider>(), authorizeData);
        Assert.NotNull(policy);

        var result = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy);
        return result.Succeeded;
    }
}
