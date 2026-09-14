using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace SocialApp.SharedKernel.Http;

/// <summary>
/// Gắn correlation ID cho mỗi request: lấy từ header <c>X-Correlation-ID</c> nếu client gửi,
/// nếu không thì sinh mới. Đặt vào <see cref="HttpContext.TraceIdentifier"/> (để traceId trong
/// Problem Details khớp), trả lại header cho client, và đẩy vào Serilog LogContext để mọi log
/// của request đều có CorrelationId.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            && !string.IsNullOrWhiteSpace(incoming)
                ? incoming.ToString()
                : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        // Gắn lúc response BẮT ĐẦU gửi, không gắn ngay: UseExceptionHandler gọi Response.Clear() (xóa mọi header) trước khi ghi
        // 500 → gắn ngay thì response 500 mất header, trong khi hợp đồng nói traceId == X-Correlation-ID (ProblemDetailsTests, D9).
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
