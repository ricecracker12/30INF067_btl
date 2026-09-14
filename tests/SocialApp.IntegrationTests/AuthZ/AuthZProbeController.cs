using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Http;
using SocialApp.SharedKernel.Results;

namespace SocialApp.IntegrationTests.AuthZ;

/// <summary>
/// Endpoint thử cho AuthZ matrix. Chỉ tồn tại trong assembly test, không bao giờ vào image.
/// /me (D7) thay được cho TC-A01/A02 sau này, nhưng không có endpoint thật nào đòi post.hide hay user.lock
/// trước GĐ6, nên probe RBAC sống lâu dài.
/// </summary>
[ApiController]
[Route("__test/authz")]
public sealed class AuthZProbeController : ControllerBase
{
    /// <summary>KHÔNG khai gì — fallback policy (C4) phải chặn nó.</summary>
    [HttpGet("no-attribute")]
    public IActionResult NoAttribute() => Ok();

    [Authorize]
    [HttpGet("authenticated")]
    public IActionResult Authenticated() => Ok();

    /// <summary>Cho JwtAuthenticationTests: principal giữ tên claim ngắn "sub"/"role" (C4).</summary>
    [Authorize]
    [HttpGet("whoami")]
    public IActionResult WhoAmI() => Ok(new { name = User.Identity?.Name, role = User.FindFirst("role")?.Value });

    [RequirePermission("post.hide")]
    [HttpGet("post-hide")]
    public IActionResult PostHide() => Ok();

    /// <summary>MODERATOR KHÔNG có user.lock (Mục 5.3) — dùng cho dòng đối chứng RBAC-02c.</summary>
    [RequirePermission("user.lock")]
    [HttpGet("user-lock")]
    public IActionResult UserLock() => Ok();

    /// <summary>
    /// Khuôn tầng 3 (C6): tài nguyên "thuộc về" <paramref name="ownerId"/>. Danh tính người gọi từ token
    /// (GetUserId), không từ route; không có nhánh Admin; từ chối bằng Result.Forbidden + ToActionResult.
    /// </summary>
    [Authorize]
    [HttpGet("owned/{ownerId:guid}")]
    public ActionResult<Guid> Owned(Guid ownerId) =>
        (ownerId == User.GetUserId() ? Result<Guid>.Success(ownerId) : Result<Guid>.Forbidden()).ToActionResult(this);
}
