namespace CinemaBooking.Web.Middleware;

/// <summary>
/// Mở một logger scope cho mỗi request để mọi dòng log bên trong đều mang sẵn TraceId,
/// đường dẫn và người dùng — không phải nhét thủ công vào từng lời gọi LogInformation.
/// </summary>
public sealed class RequestLogScopeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLogScopeMiddleware> _logger;

    public RequestLogScopeMiddleware(RequestDelegate next, ILogger<RequestLogScopeMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Dictionary (không phải chuỗi đã format) để provider JSON ghi ra từng trường riêng.
        var scope = new Dictionary<string, object?>
        {
            ["TraceId"] = context.TraceIdentifier,
            ["RequestPath"] = context.Request.Path.Value,
            ["RequestMethod"] = context.Request.Method,
            ["UserName"] = context.User.Identity?.IsAuthenticated == true
                ? context.User.Identity.Name
                : null,
        };

        using (_logger.BeginScope(scope))
        {
            await _next(context);
        }
    }
}

public static class RequestLogScopeMiddlewareExtensions
{
    /// <summary>
    /// Đặt sau UseAuthentication để scope có tên người dùng; đặt trước MVC để controller
    /// nào cũng nằm trong scope.
    /// </summary>
    public static IApplicationBuilder UseCinemaRequestLogScope(this IApplicationBuilder app)
        => app.UseMiddleware<RequestLogScopeMiddleware>();
}
