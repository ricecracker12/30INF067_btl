using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SocialApp.SharedKernel.Audit;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Redis;
using StackExchange.Redis;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Bọc handler kết quả authorization mặc định cho endpoint mang <see cref="PrivilegedEndpointAttribute"/> (GĐ6 C4):
/// <list type="bullet">
/// <item><b>503 thay 401</b> khi <c>OnTokenValidated</c> không kiểm được thu hồi (Đ-6.8) — <c>OnTokenValidated</c> chỉ đặt dấu và
/// Fail, vì từ đó chỉ ra được 401 (L-C6 của hướng dẫn khối A+C).</item>
/// <item><b>Ghi <see cref="AuditActions.AccessDenied"/></b> khi tầng 2 từ chối (Đ-6.15, US-019 AC-03), rồi trả 403 như cũ. Chống
/// ngập: tối đa một dòng mỗi phút cho mỗi (người, route template) — <c>SET NX EX 60</c>; Redis chết thì vẫn ghi (rate limit chung
/// 100/phút là trần).</item>
/// </list>
/// Chỉ <c>Forbidden</c> được ghi, không <c>Challenged</c> (401): chưa đăng nhập thì không có <c>actor_id</c>, và đó là cửa cho người
/// lạ làm ngập bảng. Endpoint không mang attribute → handler mặc định, không gì khác.
///
/// Đăng ký DUY NHẤT trong <c>AddSharedKernelAuthorization</c> — hai đăng ký <see cref="IAuthorizationMiddlewareResultHandler"/> thì
/// cái sau thắng im lặng.
/// </summary>
internal sealed class AuditingAuthorizationResultHandler(ILogger<AuditingAuthorizationResultHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    private static readonly TimeSpan DeniedWindow = TimeSpan.FromSeconds(60);

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var privileged = context.GetEndpoint()?.Metadata.GetMetadata<PrivilegedEndpointAttribute>() is not null;

        if (privileged && authorizeResult.Challenged
            && context.Items.ContainsKey(PrivilegedEndpointAttribute.RevocationUnavailableKey))
        {
            await WriteRevocationUnavailableAsync(context);
            return;
        }

        if (privileged && authorizeResult.Forbidden)
            await AuditDeniedAsync(context);

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private static async Task WriteRevocationUnavailableAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        // Qua IProblemDetailsService như mọi lỗi ngoài MVC (UseStatusCodePages) — CustomizeProblemDetails gắn traceId, instance;
        // type đặt sẵn là URN nên không bị thay bằng httpstatuses.io.
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Type = PrivilegedEndpointAttribute.RevocationUnavailableType,
                Title = "Tạm thời không kiểm tra được phiên đăng nhập",
                Detail = "Chức năng quản trị tạm thời không khả dụng. Vui lòng thử lại sau ít phút.",
            },
        });
    }

    /// <summary>
    /// Lỗi ghi audit KHÔNG đổi 403 thành 500: người bị từ chối vẫn nhận đúng 403; mất một dòng audit thì log Error để người trực
    /// thấy. <c>metadata</c> chỉ có method + route TEMPLATE — không query string, không id trên đường (Đ-6.15).
    /// </summary>
    private async Task AuditDeniedAsync(HttpContext context)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(JwtClaims.Sub), out var actorId))
            return;

        var routeTemplate = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(không rõ)";

        try
        {
            if (!await FirstInWindowAsync(context, actorId, routeTemplate))
                return;

            var audit = context.RequestServices.GetRequiredService<IAuditTrail>();
            await audit.AppendAsync(null, new AuditEntry(
                actorId, AuditActions.AccessDenied, "endpoint", null,
                new Dictionary<string, object?> { ["method"] = context.Request.Method, ["routeTemplate"] = routeTemplate }),
                context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Không ghi được audit access.denied cho route {RouteTemplate}", routeTemplate);
        }
    }

    /// <summary><c>true</c> nếu đây là lần từ chối đầu tiên của (người, route) trong 60 giây — hoặc Redis không trả lời được.</summary>
    private static async Task<bool> FirstInWindowAsync(HttpContext context, Guid actorId, string routeTemplate)
    {
        if (context.RequestServices.GetService<RedisConnection>()?.ConnectedOrNull() is not { } redis)
            return true;

        try
        {
            return await redis.GetDatabase().StringSetAsync(
                $"audit:denied:{actorId:D}:{routeTemplate}", 1, DeniedWindow, When.NotExists);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            return true;
        }
    }
}
