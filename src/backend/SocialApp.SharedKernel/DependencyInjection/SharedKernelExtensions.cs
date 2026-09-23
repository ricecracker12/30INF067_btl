using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Errors;
using SocialApp.SharedKernel.Events;
using SocialApp.SharedKernel.Http;

namespace SocialApp.SharedKernel.DependencyInjection;

/// <summary>
/// Ráp toàn bộ hạ tầng dùng chung của SharedKernel: RFC 7807 Problem Details (kèm traceId),
/// exception handler toàn cục, rate limiting (fixed window), và event bus trong tiến trình (Đ-6.2). Api gọi <see cref="AddSharedKernel"/>
/// khi cấu hình DI, rồi <see cref="UseSharedKernel"/> và <see cref="UseSharedKernelRateLimiter"/>
/// trong pipeline — hai lệnh riêng vì thứ tự của chúng so với UseAuthentication không giống nhau.
/// </summary>
public static class SharedKernelExtensions
{
    /// <summary>Tên policy rate limit chặt cho nhóm endpoint xác thực (10 req/phút).</summary>
    public const string AuthRateLimitPolicy = "auth";

    public static IServiceCollection AddSharedKernel(this IServiceCollection services)
    {
        // RFC 7807: mọi ProblemDetails đều có traceId (= correlation id), instance và type theo hợp đồng.
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
                ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;

                // Framework điền link rfc9110 (ProblemDetailsDefaults, ClientErrorMapping mặc định) TRƯỚC khi hàm này chạy, nên
                // `Type ??=` không bao giờ kích hoạt — 401/409/500 từng ra tools.ietf.org thay vì type của hợp đồng. Thay link của
                // framework; type do code tự đặt (khác tools.ietf.org) thì giữ.
                if (ctx.ProblemDetails.Type is null
                    || ctx.ProblemDetails.Type.StartsWith("https://tools.ietf.org/", StringComparison.Ordinal))
                    ctx.ProblemDetails.Type = ProblemTitles.TypeFor(
                        ctx.ProblemDetails.Status ?? ctx.HttpContext.Response.StatusCode);
            };
        });

        // MỘT factory cho mọi Problem Details do MVC sinh (Problem(), ValidationProblem(), 400 validation tự động, 415/404 của
        // ClientErrorResultFilter): title/type mặc định + errors đã làm sạch. Replace chứ không TryAdd — đúng dù gọi trước hay sau
        // AddControllers (MVC chỉ TryAdd factory mặc định). Dùng factory thay vì chỉnh InvalidModelStateResponseFactory: controller
        // gọi ValidationProblem(ModelState) hay Problem() sẽ đi vòng qua response factory đó.
        services.Replace(ServiceDescriptor.Singleton<ProblemDetailsFactory, SharedKernelProblemDetailsFactory>());

        // Lớp chặn thứ nhất: System.Text.Json không đưa thông điệp gốc vào ModelState (lộ tên kiểu .NET nội bộ, vị trí byte, tiếng
        // Anh). Lớp thứ hai là ValidationErrors — vẫn thay mọi lỗi ở đường dẫn JSON nếu ai đó bật lại cờ này.
        services.PostConfigure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => options.AllowInputFormatterExceptionMessages = false);

        // Thông điệp model binding của MVC — tiếng Việt, không lặp lại giá trị client gửi (ValidationErrors.Localize).
        services.PostConfigure<MvcOptions>(options => ValidationErrors.Localize(options.ModelBindingMessageProvider));

        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Mặc định toàn cục: 100 req/phút, phân vùng theo user (nếu đã đăng nhập) hoặc IP.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKey(ctx),
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

        // Đ-6.2, Đ-6.4: event bus ở đây để Program.cs không có dòng riêng — một chỗ đụng nhau ít hơn với GĐ3, GĐ5 (Mục 9.4).
        services.AddInProcessEventBus();

        return services;
    }

    /// <summary>
    /// Phần pipeline phải chạy SỚM NHẤT, trước mọi thứ khác: correlation ID rồi exception handler, rồi
    /// status code pages. Rate limiter KHÔNG nằm ở đây — xem <see cref="UseSharedKernelRateLimiter"/>.
    /// </summary>
    public static IApplicationBuilder UseSharedKernel(this IApplicationBuilder app)
    {
        // Correlation ID chạy sớm nhất để mọi log (kể cả khi xử lý exception) đều có nó.
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();

        // 401 của JwtBearer và 403 của authorization có BODY RỖNG. Đã AddProblemDetails nên middleware này
        // sinh ProblemDetails (kèm traceId qua CustomizeProblemDetails) cho mọi response 4xx/5xx chưa có body
        // — AGENTS.md Mục 9. AuthZ matrix khẳng định problem+json cho mọi dòng 401/403.
        // Handler riêng thay mặc định chỉ để đặt title tiếng Việt: mặc định ghi "Unauthorized"/"Too Many Requests"
        // (ProblemDetailsDefaults điền TRƯỚC CustomizeProblemDetails, nên không sửa được ở đó).
        app.UseStatusCodePages(async context =>
        {
            var http = context.HttpContext;
            var status = http.Response.StatusCode;
            await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = http,
                ProblemDetails = new ProblemDetails { Status = status, Title = ProblemTitles.For(status) },
            });
        });
        return app;
    }

    /// <summary>
    /// Bật rate limiter. <b>PHẢI gọi SAU <c>UseAuthentication()</c></b>.
    ///
    /// Lý do: <see cref="RateLimitPartitionKey"/> phân vùng theo user khi đã đăng nhập, chỉ rơi về IP khi
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

    /// <summary>
    /// Khóa phân vùng rate limit: đã đăng nhập → theo claim sub; chưa → theo IP.
    /// Đọc THẲNG claim sub, không qua Identity.Name: Name phụ thuộc NameClaimType của JwtBearer, quên cấu
    /// hình là mọi user chung một hạn mức mà không có lỗi nào (RateLimitPartitionKeyTests).
    /// </summary>
    public static string RateLimitPartitionKey(HttpContext ctx) =>
        ctx.User.Identity?.IsAuthenticated == true && ctx.User.FindFirstValue(JwtClaims.Sub) is { } sub
            ? $"user:{sub}"
            : $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "anon"}";
}
