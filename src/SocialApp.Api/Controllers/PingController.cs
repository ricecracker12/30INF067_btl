using Microsoft.AspNetCore.Mvc;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.Api.Controllers;

/// <summary>
/// Endpoint mẫu của Walking Skeleton (GĐ0): đi hết pipeline (routing → rate limit → controller →
/// JSON). Kèm 2 endpoint demo để kiểm chứng RFC 7807 (lỗi nghiệp vụ và lỗi không mong muốn).
/// </summary>
[ApiController]
[Route("api/v1/ping")]
[ApiExplorerSettings(GroupName = ApiGroup)]
public sealed class PingController : ControllerBase
{
    /// <summary>
    /// Nhóm Swagger cho các endpoint hạ tầng do chính host giữ (không thuộc module nào).
    /// Bắt buộc phải có: DocInclusionPredicate lọc theo GroupName, controller không khai nhóm sẽ
    /// rơi khỏi MỌI trang Swagger — im lặng, không lỗi.
    /// </summary>
    public const string ApiGroup = "platform-v1";

    [HttpGet]
    public IActionResult Get() => Ok(new PingResponse("pong", HttpContext.TraceIdentifier));

    /// <summary>Demo AppException → Problem Details 409 (có detail + traceId).</summary>
    [HttpGet("app-error")]
    public IActionResult AppError() =>
        throw AppException.Conflict("Đây là lỗi nghiệp vụ mẫu để kiểm chứng RFC 7807.");

    /// <summary>Demo exception chưa xử lý → Problem Details 500 (giấu chi tiết nội bộ).</summary>
    [HttpGet("boom")]
    public IActionResult Boom() =>
        throw new InvalidOperationException("Lỗi nội bộ mẫu — client không nên thấy message này.");
}

public sealed record PingResponse(string Message, string TraceId);
