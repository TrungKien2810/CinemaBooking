using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CinemaBooking.Infrastructure.Logging;

/// <summary>
/// Cấu hình log dùng chung cho cả Web lẫn Worker: log có cấu trúc, có scope, có TraceId.
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>
    /// Development: console một dòng cho dễ đọc khi debug.
    /// Ngoài Development: console JSON để công cụ thu thập log parse được thành trường,
    /// thay vì phải regex một dòng chữ.
    /// </summary>
    public static ILoggingBuilder AddCinemaLogging(this ILoggingBuilder logging, bool isDevelopment)
    {
        logging.ClearProviders();

        // Gắn TraceId/SpanId của Activity vào mọi log ⇒ ghép được các dòng log của cùng một
        // request, kể cả khi nó đi từ Web sang Worker qua outbox.
        logging.Configure(o => o.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId
            | ActivityTrackingOptions.SpanId
            | ActivityTrackingOptions.ParentId);

        if (isDevelopment)
        {
            logging.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.IncludeScopes = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
            logging.AddDebug();
        }
        else
        {
            logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = true;
                o.UseUtcTimestamp = true;
                // Một dòng JSON cho một bản ghi: bắt buộc để log agent đọc được theo dòng.
                o.JsonWriterOptions = new JsonWriterOptions { Indented = false };
            });
        }

        return logging;
    }
}
