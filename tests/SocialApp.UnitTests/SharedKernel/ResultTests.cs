using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.SharedKernel.DependencyInjection;
using SocialApp.SharedKernel.Errors;
using SocialApp.SharedKernel.Http;
using SocialApp.SharedKernel.Results;
using Xunit;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>Smoke test GĐ0 cho Result types — xác nhận khung unit test chạy được trên CI.</summary>
public sealed class ResultTests
{
    [Fact]
    public void Success_result_has_value_and_no_error()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_result_carries_error()
    {
        var error = new Error("post.not_found", "Không tìm thấy bài viết", 404);
        Result<int> result = error;

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    /// <summary>
    /// Q-D4 (D0): hình dạng của <see cref="Error.Validation"/> trước khi ai đó dùng nó ở D3/D5/D7. Status 400 và Message
    /// bằng <see cref="ProblemTitles.ValidationDetail"/> là thứ làm 400 sau I/O trông GIỐNG HỆT 400 của FluentValidation —
    /// đổi một trong hai là FE thấy hai hình dạng cho cùng một loại lỗi.
    /// </summary>
    [Fact]
    public void Validation_error_mang_dung_field_va_status_400()
    {
        var error = Error.Validation("mediaKey", "m");

        Assert.Equal(400, error.Status);
        Assert.Equal(ProblemTitles.ValidationDetail, error.Message);
        Assert.Null(error.Title);   // để SharedKernelProblemDetailsFactory điền "Dữ liệu không hợp lệ" theo status
        Assert.NotNull(error.Errors);
        Assert.Equal(["m"], error.Errors!["mediaKey"]);
    }

    /// <summary>
    /// Lỗi KHÔNG phải validation vẫn không mang <c>errors</c> — tham số mới của <see cref="Error"/> có mặc định nên mọi
    /// lời gọi vị trí của GĐ1 (<c>IdentityErrors</c>) giữ nguyên hành vi. Đây là vế "tương thích ngược" của Q-D4.
    /// </summary>
    [Fact]
    public void Loi_thuong_khong_mang_errors()
    {
        Assert.Null(Error.Forbidden.Errors);
        Assert.Null(new Error("post.not_found", "Không tìm thấy bài viết.", 404).Errors);
    }

    /// <summary>
    /// Vế thứ hai của Q-D4: <c>ToActionResult</c> biến <see cref="Error.Validation"/> thành 400 CÓ <c>errors</c> theo đúng
    /// key, đi qua chính <c>SharedKernelProblemDetailsFactory</c> mà [ApiController] dùng — nên title là "Dữ liệu không hợp
    /// lệ" và detail là ValidationDetail mà không ai phải gõ tay.
    /// </summary>
    [Fact]
    public void ToActionResult_cua_Validation_ra_400_kem_errors()
    {
        var result = Error.Validation("mediaKey", "Ảnh chưa được tải lên xong.")
            .ToActionResult(NewController());

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(bad.Value);

        Assert.Equal(400, problem.Status);
        Assert.Equal(ProblemTitles.BadRequest, problem.Title);
        Assert.Equal(ProblemTitles.ValidationDetail, problem.Detail);
        Assert.Equal(["Ảnh chưa được tải lên xong."], problem.Errors["mediaKey"]);
    }

    /// <summary>Vế đối chứng: lỗi không mang <c>errors</c> vẫn đi đường <c>Problem()</c> cũ — không có <c>errors</c> nào mọc ra.</summary>
    [Fact]
    public void ToActionResult_cua_loi_thuong_van_la_ProblemDetails_khong_co_errors()
    {
        var result = Error.Forbidden.ToActionResult(NewController());

        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);

        Assert.Equal(403, problem.Status);
        Assert.Equal(ProblemTitles.Forbidden, problem.Title);
        Assert.IsNotType<ValidationProblemDetails>(problem);
    }

    /// <summary>
    /// Controller trần đủ để gọi <c>Problem()</c>/<c>ValidationProblem()</c>: cả hai lấy <c>ProblemDetailsFactory</c> từ
    /// <c>HttpContext.RequestServices</c>. Dùng <c>AddSharedKernel</c> thật chứ không giả factory — test này tồn tại để
    /// khẳng định đường đi qua factory CỦA REPO, giả nó đi là mất luôn thứ cần kiểm.
    /// </summary>
    private static ControllerBase NewController()
    {
        var services = new ServiceCollection().AddSharedKernel().BuildServiceProvider();
        return new ProbeController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services },
            },
        };
    }

    private sealed class ProbeController : ControllerBase;
}
