using System.Text.Json;
using CinemaBooking.Web.Middleware;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace CinemaBooking.Tests;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task Business_exception_becomes_400_problem_details_for_json_clients()
    {
        var context = JsonRequest();

        await Run(context, _ => throw new ShowtimeSchedulingException("Không tìm thấy phim #7."));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        var problem = await ReadJson(context);
        Assert.Equal("Không tìm thấy phim #7.", problem.GetProperty("title").GetString());
        Assert.Equal(context.TraceIdentifier, problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Unknown_exception_becomes_500_without_leaking_details_outside_development()
    {
        var context = JsonRequest();

        await Run(context, _ => throw new InvalidOperationException("connection string sai"), isDevelopment: false);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        var problem = await ReadJson(context);
        Assert.DoesNotContain("connection string", problem.ToString());
        Assert.False(problem.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task Browser_request_is_re_executed_into_the_error_page_keeping_the_status()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/Booking/SeatMap/12";
        context.Request.Headers.Accept = "text/html";
        context.Response.Body = new MemoryStream();

        var seenPaths = new List<string>();
        await Run(context, ctx =>
        {
            seenPaths.Add(ctx.Request.Path);
            if (seenPaths.Count == 1) throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        });

        Assert.Equal(new[] { "/Booking/SeatMap/12", "/Home/Error" }, seenPaths);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains("traceId", context.Request.QueryString.Value);
    }

    [Fact]
    public async Task Error_page_throwing_again_does_not_loop_forever()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Accept = "text/html";
        context.Response.Body = new MemoryStream();

        var calls = 0;
        await Run(context, _ =>
        {
            calls++;
            throw new InvalidOperationException("boom");
        });

        // Lần 1: request gốc. Lần 2: trang lỗi, cũng hỏng ⇒ dừng, không gọi lần 3.
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Client_disconnect_is_not_logged_as_an_error()
    {
        var context = new DefaultHttpContext();
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        context.RequestAborted = aborted.Token;

        await Run(context, _ => throw new OperationCanceledException(aborted.Token));

        // Không đụng tới status: request coi như kết thúc bình thường.
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static DefaultHttpContext JsonRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/showtimes";
        context.Request.Headers.Accept = "application/json";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static Task Run(HttpContext context, RequestDelegate next, bool isDevelopment = true)
    {
        var sut = new ExceptionHandlingMiddleware(
            next,
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            new FakeHostEnvironment(isDevelopment ? Environments.Development : Environments.Production));

        return sut.InvokeAsync(context);
    }

    private static async Task<JsonElement> ReadJson(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        return doc.RootElement.Clone();
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "CinemaBooking.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
