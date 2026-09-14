using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SocialApp.Modules.Identity.Application.Registration;
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
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting(SharedKernelExtensions.AuthRateLimitPolicy)]   // cả nhóm auth: 10 req/phút/IP (ISS-04)
[ApiExplorerSettings(GroupName = IdentityApiGroup.Name)]
public sealed class AuthController(RegistrationService registration) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    [Consumes("application/json")]   // không đặt ở class: refresh/logout không có body
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
    [Consumes("application/json")]
    [ProducesResponseType<VerifyEmailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone, "application/problem+json")]
    public async Task<ActionResult<VerifyEmailResponse>> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        // 400 có hai nguồn, cùng mã trong hợp đồng: sai định dạng (validator, có `errors`) và không tồn tại (Error).
        var result = await registration.VerifyEmailAsync(request, ct);
        return result.ToActionResult(this);
    }
}
