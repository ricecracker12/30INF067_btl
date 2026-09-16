using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SocialApp.Modules.Identity.Application.Login;
using SocialApp.Modules.Identity.Application.Registration;
using SocialApp.Modules.Identity.Application.Session;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.DependencyInjection;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Identity.Presentation;

/// <summary>
/// Nhóm <c>/auth</c> của hợp đồng identity-v1. Controller mỏng: gọi service ở Application, ánh xạ <c>Result → HTTP</c>.
///
/// <c>[AllowAnonymous]</c> đặt ở TỪNG action công khai, không ở class: đặt ở class thì <c>[Authorize]</c> của logout (D6)
/// vô hiệu — AllowAnonymous thắng mọi Authorize — và endpoint cần token mở toang.
///
/// Không khai <c>[Produces("application/json")]</c> ở class: filter đó ép content type của CẢ lỗi thành
/// application/json, trong khi hợp đồng yêu cầu application/problem+json.
///
/// Không khai <c>[Consumes("application/json")]</c> (bỏ ở D9): nó loại action ngay lúc CHỌN ENDPOINT, request sai content type rơi
/// vào endpoint 415 nội bộ không có <c>[AllowAnonymous]</c> → gọi ẩn danh nhận 401 "Chưa xác thực" thay vì 415. Không có nó, input
/// formatter JSON tự trả 415 SAU bước phân quyền, và Swagger vẫn ghi request body <c>application/json</c>.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting(SharedKernelExtensions.AuthRateLimitPolicy)]   // cả nhóm auth: 10 req/phút/IP (ISS-04)
[ApiExplorerSettings(GroupName = IdentityApiGroup.Name)]
public sealed class AuthController(
    RegistrationService registration,
    LoginService login,
    SessionService sessions,
    IOptions<JwtOptions> jwt) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<RegisterResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await registration.RegisterAsync(request, ct);

        // ToActionResult trả 200 khi thành công — register là 201. Không CreatedAtAction: không có GET /users/{id}.
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult(this);
    }

    [AllowAnonymous]
    [HttpPost("verify-email")]
    [ProducesResponseType<VerifyEmailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone, "application/problem+json")]
    public async Task<ActionResult<VerifyEmailResponse>> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        // 400 có hai nguồn, cùng mã trong hợp đồng: sai định dạng (validator, có `errors`) và không tồn tại (Error).
        var result = await registration.VerifyEmailAsync(request, ct);
        return result.ToActionResult(this);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked, "application/problem+json")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        // IP chỉ để điều tra (refresh_tokens.created_ip). Sau reverse proxy đây là IP của proxy — chấp nhận ở GĐ1;
        // bật ForwardedHeaders là việc của F1 nếu cần.
        var result = await login.LoginAsync(request, HttpContext.Connection.RemoteIpAddress, ct);
        if (result.IsFailure)
            return result.Error!.Value.ToActionResult(this);

        RefreshCookie.Set(Response, result.Value!.RefreshPlain, jwt.Value.RefreshTokenDays);
        return Ok(new TokenResponse(result.Value.Access.Token, result.Value.Access.ExpiresIn));
    }

    /// <summary>
    /// KHÔNG nhận body — refresh token đọc từ cookie (quyết định 6). <c>[AllowAnonymous]</c>: xác thực bằng cookie, không bằng
    /// bearer — access token lúc này thường đã hết hạn. Mọi 401 đều xóa cookie để FE không thử lại bằng token đã chết.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<TokenResponse>> Refresh(CancellationToken ct)
    {
        var result = await sessions.RefreshAsync(RefreshCookie.Read(Request), HttpContext.Connection.RemoteIpAddress, ct);
        if (result.IsFailure)
        {
            RefreshCookie.Clear(Response);
            return result.Error!.Value.ToActionResult(this);
        }

        RefreshCookie.Set(Response, result.Value!.RefreshPlain, jwt.Value.RefreshTokenDays);
        return Ok(new TokenResponse(result.Value.Access.Token, result.Value.Access.ExpiresIn));
    }

    /// <summary>
    /// Cần bearer (<c>[Authorize]</c>, không AllowAnonymous): danh tính người đăng xuất lấy từ access token, cookie chỉ nói
    /// family nào. Luôn 204 + xóa cookie (Đ-D6); 401 chỉ khi bearer thiếu/hỏng. Access token đang cầm vẫn sống tới hết hạn —
    /// client tự xóa khỏi memory (hợp đồng đã ghi).
    /// </summary>
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await sessions.LogoutAsync(RefreshCookie.Read(Request), User.GetUserId(), ct);
        RefreshCookie.Clear(Response);
        return NoContent();
    }
}
