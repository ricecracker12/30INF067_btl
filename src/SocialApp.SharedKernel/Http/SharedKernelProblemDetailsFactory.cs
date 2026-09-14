using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.SharedKernel.Http;

/// <summary>
/// Thay <c>DefaultProblemDetailsFactory</c> của MVC — điểm mở rộng chính thức, và là chỗ DUY NHẤT mọi Problem Details do MVC sinh
/// đi qua: <c>Problem()</c> (gồm <c>ToActionResult</c>), <c>ValidationProblem()</c>, 400 validation tự động của [ApiController],
/// 415/404… của ClientErrorResultFilter. Module GĐ2+ dùng các đường đó là tự có title/type/errors đúng hợp đồng, không phải nhớ
/// cấu hình gì.
///
/// Khác mặc định: title/type theo <see cref="ProblemTitles"/> thay link rfc9110 + reason phrase tiếng Anh; <c>errors</c> qua
/// <see cref="ValidationErrors"/>; <c>detail</c> mặc định của 400 là <see cref="ProblemTitles.ValidationDetail"/>. traceId/instance
/// vẫn do <c>CustomizeProblemDetails</c> (AddSharedKernel) gắn — gọi nó như factory mặc định. Status code pages và
/// GlobalExceptionHandler KHÔNG đi qua đây (IProblemDetailsService) — xem UseSharedKernel.
/// </summary>
internal sealed class SharedKernelProblemDetailsFactory(IOptions<ProblemDetailsOptions> options) : ProblemDetailsFactory
{
    public override ProblemDetails CreateProblemDetails(
        HttpContext httpContext, int? statusCode = null, string? title = null, string? type = null, string? detail = null,
        string? instance = null)
    {
        var status = statusCode ?? StatusCodes.Status500InternalServerError;
        return Customize(httpContext, new ProblemDetails
        {
            Status = status,
            Title = title ?? ProblemTitles.For(status),
            Type = type ?? ProblemTitles.TypeFor(status),
            Detail = detail,
            Instance = instance,
        });
    }

    public override ValidationProblemDetails CreateValidationProblemDetails(
        HttpContext httpContext, ModelStateDictionary modelStateDictionary, int? statusCode = null, string? title = null,
        string? type = null, string? detail = null, string? instance = null)
    {
        var status = statusCode ?? StatusCodes.Status400BadRequest;
        return Customize(httpContext, new ValidationProblemDetails(ValidationErrors.From(modelStateDictionary))
        {
            Status = status,
            Title = title ?? ProblemTitles.For(status),
            Type = type ?? ProblemTitles.TypeFor(status),
            Detail = detail ?? ProblemTitles.ValidationDetail,
            Instance = instance,
        });
    }

    private T Customize<T>(HttpContext httpContext, T problem) where T : ProblemDetails
    {
        options.Value.CustomizeProblemDetails?.Invoke(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
        return problem;
    }
}
