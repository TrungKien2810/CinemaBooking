using System.Net.Mime;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CinemaBooking.Web.Middleware;

/// <summary>
/// Chốt chặn cuối cho mọi exception chưa được bắt: ghi log kèm TraceId rồi trả về câu trả lời
/// đúng định dạng mà client đang chờ — ProblemDetails cho fetch/SignalR, trang lỗi cho trình duyệt.
/// Mục tiêu là khách không bao giờ nhìn thấy stack trace, còn đội dev luôn tra được log theo TraceId.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    /// <summary>Đánh dấu request đã đi qua nhánh xử lý lỗi một lần rồi.</summary>
    private const string ReentryKey = "__cinema_error_reentry";

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(
        RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Khách bấm Back hoặc đóng tab giữa chừng — không phải lỗi, đừng làm bẩn log.
            _logger.LogDebug("Request {Path} bị huỷ bởi client.", context.Request.Path);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var traceId = context.TraceIdentifier;
        var (status, title) = Classify(ex);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(ex, "Lỗi chưa xử lý tại {Method} {Path} (TraceId {TraceId}).",
                context.Request.Method, context.Request.Path, traceId);
        }
        else
        {
            _logger.LogWarning(ex, "Yêu cầu không hợp lệ tại {Method} {Path} (TraceId {TraceId}).",
                context.Request.Method, context.Request.Path, traceId);
        }

        // Đã gửi byte đầu tiên thì không đổi được status/body nữa — chỉ còn cách cắt kết nối.
        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response đã bắt đầu, không thể trả trang lỗi cho TraceId {TraceId}.", traceId);
            context.Abort();
            return;
        }

        // Nếu chính trang lỗi cũng ném exception thì không được chạy lại lần nữa, nếu không
        // middleware sẽ tự gọi mình vô hạn cho tới khi tràn stack.
        if (context.Items.ContainsKey(ReentryKey))
        {
            _logger.LogError(ex, "Trang lỗi cũng thất bại (TraceId {TraceId}).", traceId);
            context.Abort();
            return;
        }

        context.Items[ReentryKey] = true;

        context.Response.Clear();
        context.Response.StatusCode = status;

        if (WantsJson(context.Request))
        {
            var problem = new ProblemDetails
            {
                Status = status,
                Title = title,
                Instance = context.Request.Path,
                // Chi tiết kỹ thuật chỉ lộ ở Development; Production chỉ đưa TraceId để đối chiếu log.
                Detail = _env.IsDevelopment() ? ex.ToString() : null,
            };
            problem.Extensions["traceId"] = traceId;

            context.Response.ContentType = MediaTypeNames.Application.Json;
            await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
            return;
        }

        // Không redirect: 302 sẽ biến lỗi 500 thành 200 ở request kế tiếp, hỏng cả status trả về
        // lẫn số liệu giám sát. Thay vào đó chạy lại pipeline vào trang lỗi và giữ nguyên status —
        // đúng cách ExceptionHandlerMiddleware của framework vẫn làm.
        context.Request.Path = "/Home/Error";
        context.Request.QueryString = QueryString.Create("traceId", traceId);
        context.Request.Method = HttpMethods.Get;

        // Endpoint và route values của request cũ phải xoá, nếu không UseRouting sẽ dùng lại.
        context.SetEndpoint(null);
        context.Request.RouteValues.Clear();

        try
        {
            await _next(context);
        }
        catch (Exception errorPageException)
        {
            // Trang lỗi cũng hỏng — vào lại HandleAsync, lần này guard ReentryKey sẽ chặn.
            await HandleAsync(context, errorPageException);
        }
    }

    /// <summary>
    /// Lỗi nghiệp vụ đã biết thì trả 400 để client hiển thị thông báo; còn lại là 500 thật sự.
    /// </summary>
    private static (int Status, string Title) Classify(Exception ex) => ex switch
    {
        ShowtimeSchedulingException => (StatusCodes.Status400BadRequest, ex.Message),
        ArgumentException => (StatusCodes.Status400BadRequest, "Dữ liệu gửi lên không hợp lệ."),
        _ => (StatusCodes.Status500InternalServerError, "Hệ thống đang gặp sự cố. Vui lòng thử lại."),
    };

    private static bool WantsJson(HttpRequest request)
    {
        if (request.Path.StartsWithSegments("/api") || request.Path.StartsWithSegments("/hubs"))
            return true;

        // fetch() của trang chọn ghế gửi X-Requested-With; form post thường thì không.
        if (request.Headers.XRequestedWith == "XMLHttpRequest")
            return true;

        var accept = request.Headers.Accept.ToString();
        return accept.Contains(MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase)
            && !accept.Contains(MediaTypeNames.Text.Html, StringComparison.OrdinalIgnoreCase);
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    /// <summary>
    /// Phải đăng ký SỚM NHẤT trong pipeline: middleware chỉ bắt được exception của những
    /// middleware nằm SAU nó.
    /// </summary>
    public static IApplicationBuilder UseCinemaExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ExceptionHandlingMiddleware>();
}
