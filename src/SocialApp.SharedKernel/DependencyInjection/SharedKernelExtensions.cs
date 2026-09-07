using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.SharedKernel.Errors;
using SocialApp.SharedKernel.Http;

namespace SocialApp.SharedKernel.DependencyInjection;

/// <summary>
/// Ráp toàn bộ hạ tầng dùng chung của SharedKernel: RFC 7807 Problem Details (kèm traceId),
/// exception handler toàn cục, và rate limiting (fixed window). Api gọi <see cref="AddSharedKernel"/>
/// khi cấu hình DI, rồi <see cref="UseSharedKernel"/> và <see cref="UseSharedKernelRateLimiter"/>
/// trong pipeline — hai lệnh riêng vì thứ tự của chúng so với UseAuthentication không giống nhau.
/// </summary>
public static class SharedKernelExtensions
{
    /// <summary>Tên policy rate limit chặt cho nhóm endpoint xác thực (10 req/phút).</summary>
    public const string AuthRateLimitPolicy = "auth";

    public static IServiceCollection AddSharedKernel(this IServiceCollection services)
    {
        // RFC 7807: mọi ProblemDetails đều có traceId (= correlation id), instance và type mặc định.
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
                ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
                ctx.ProblemDetails.Type ??=
                    $"https://httpstatuses.io/{ctx.ProblemDetails.Status ?? StatusCodes.Status500InternalServerError}";
            };
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Mặc định toàn cục: 100 req/phút, phân vùng theo user (nếu đã đăng nhập) hoặc IP.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(ctx),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            // Policy chặt cho auth (đăng ký/đăng nhập): 10 req/phút theo IP — chống brute force (ISS-04).
            options.AddPolicy(AuthRateLimitPolicy, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }

    /// <summary>
    /// Phần pipeline phải chạy SỚM NHẤT, trước mọi thứ khác: correlation ID rồi exception handler.
    /// Rate limiter KHÔNG nằm ở đây — xem <see cref="UseSharedKernelRateLimiter"/>.
    /// </summary>
    public static IApplicationBuilder UseSharedKernel(this IApplicationBuilder app)
    {
        // Correlation ID chạy sớm nhất để mọi log (kể cả khi xử lý exception) đều có nó.
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        return app;
    }

    /// <summary>
    /// Bật rate limiter. <b>PHẢI gọi SAU <c>UseAuthentication()</c></b> (GĐ1 thêm tầng 1 AuthN).
    ///
    /// Lý do: <see cref="PartitionKey"/> phân vùng theo user khi đã đăng nhập, chỉ rơi về IP khi
    /// chưa. Đặt limiter trước UseAuthentication thì <c>ctx.User</c> luôn rỗng ở thời điểm limiter
    /// chạy → mọi request đều bị phân vùng theo IP. Hỏng câm: không exception, không log, chỉ là
    /// nhiều user sau cùng một NAT/proxy ăn chung hạn mức 100 req/phút của nhau. Không test tự động
    /// nào bắt được, nên tách thành lệnh riêng để thứ tự hiện ra ngay ở Program.cs.
    ///
    /// Đánh đổi đã cân nhắc: xác thực JWT chạy trước khi limiter từ chối request. Chấp nhận được vì
    /// việc đó rẻ (kiểm chữ ký trong bộ nhớ) — còn nhóm endpoint đắt tiền thật (login/register, BCrypt
    /// cost 12) thì dùng policy <see cref="AuthRateLimitPolicy"/> phân vùng theo IP nên không phụ
    /// thuộc thứ tự này.
    /// </summary>
    public static IApplicationBuilder UseSharedKernelRateLimiter(this IApplicationBuilder app)
    {
        app.UseRateLimiter();
        return app;
    }

    private static string PartitionKey(HttpContext ctx) =>
        ctx.User.Identity?.IsAuthenticated == true
            ? $"user:{ctx.User.Identity.Name}"
            : $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "anon"}";
}
