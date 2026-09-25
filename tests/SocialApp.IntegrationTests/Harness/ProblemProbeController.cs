using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocialApp.SharedKernel.Http;
using SocialApp.SharedKernel.Results;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// Endpoint thử cho <c>ProblemDetailsTests</c> (GĐ6 D5, L-D11): một <see cref="Error"/> mang <c>Extensions</c> đi qua
/// <see cref="ResultHttpExtensions"/> THẬT. Chỉ tồn tại trong assembly test — nạp bằng <c>AddApplicationPart</c> ở chính ca test,
/// không vào image, không vào Swagger (không <c>[ApiExplorerSettings]</c> nhóm nào).
/// </summary>
[ApiController]
[Route("__test/problem")]
[AllowAnonymous]
public sealed class ProblemProbeController : ControllerBase
{
    public static readonly Error WithExtensions = new(
        "probe.extensions", "Cần xác nhận.", 409, "Cần xác nhận", Type: "urn:socialapp:problem:probe",
        Extensions: new Dictionary<string, object?>
        {
            ["added"] = new[] { "a.b" },
            ["removed"] = Array.Empty<string>(),
            ["affectedUsers"] = 3,
        });

    [HttpGet("extensions")]
    public IActionResult Extensions() => WithExtensions.ToActionResult(this);
}
