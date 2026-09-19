using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SocialApp.SharedKernel.Results;

namespace SocialApp.SharedKernel.Http;

/// <summary>
/// Result → HTTP ở MỘT chỗ, ánh xạ tổng quát theo <see cref="Error.Status"/> (403 của tầng 3, và 404/409 của
/// khối D dùng chung). Controller gọi <c>return result.ToActionResult(this);</c>.
///
/// Vì sao là extension method (Đ5): không ném exception cho luồng nghiệp vụ bình thường (Mục 6.3 quy ước 2) —
/// mỗi lần có người dò IDOR không được thành một stack trace trong log; không phải middleware vì Result không
/// phải exception nên middleware không thấy nó; không phải filter vì filter bắt Result&lt;T&gt; làm Swagger sinh
/// schema Result&lt;T&gt; thay DTO → cổng hợp đồng đỏ. <c>ControllerBase.Problem</c> đi qua ProblemDetailsFactory
/// nên traceId được gắn như mọi lỗi khác. KHÔNG thêm Error.Code vào ProblemDetails — hợp đồng đã chốt
/// <c>{type,title,status,errors,traceId}</c>.
///
/// Khuôn tầng 3 cho GĐ2+ (chép nguyên hình dạng này):
/// <code>
/// // Tầng Application của module. actorId do controller truyền vào từ User.GetUserId() — KHÔNG từ route/body.
/// public async Task&lt;Result&gt; UpdateAsync(Guid postId, Guid actorId, UpdatePostRequest req, CancellationToken ct)
/// {
///     var post = await _posts.FindAsync(postId, ct);
///
///     // Tầng 3. KHÔNG có nhánh "if role == ADMIN" ở đây (Mục 3.2).
///     // Thao tác ghi cần ownership → CÙNG 403 cho "không tồn tại" và "không phải của bạn" (quy ước 3b).
///     if (post is null || post.AuthorId != actorId)
///         return Result.Forbidden();
///
///     // ...
/// }
/// </code>
///
/// Lỗi theo TRƯỜNG phát hiện sau I/O → <see cref="Error.Validation"/> (Q-D4, chốt 2026-09-19), không phải
/// <c>ModelState.AddModelError</c> ở controller và không phải <c>AppException.Validation</c>:
/// <code>
/// // Sau HEAD lên R2 — thứ FluentValidation không kiểm được vì cần I/O.
/// var head = await storage.HeadAsync(key, ct);
/// if (head is null)
///     return ProfileErrors.AvatarNotUploaded;   // Error.Validation("mediaKey", "Ảnh chưa được tải lên xong…")
/// </code>
/// 400 sinh ra đi qua CÙNG <c>ValidationProblemDetails</c> mà [ApiController] dùng cho 400 của FluentValidation, nên
/// title/type/detail/traceId giống hệt — FE không bao giờ thấy hai hình dạng cho cùng một loại lỗi.
///
/// Mỗi endpoint như vậy PHẢI có dòng trong AuthZMatrix.cs (AGENTS.md Mục 10).
/// </summary>
public static class ResultHttpExtensions
{
    public static IActionResult ToActionResult(this Result result, ControllerBase controller) =>
        result.IsSuccess ? controller.NoContent() : Problem(controller, result.Error!.Value);

    public static ActionResult<T> ToActionResult<T>(this Result<T> result, ControllerBase controller) =>
        result.IsSuccess ? controller.Ok(result.Value) : Problem(controller, result.Error!.Value);

    /// <summary>
    /// Chỉ phần lỗi — cho action phải làm thêm việc khi thành công (set cookie ở login/refresh, trả DTO khác kiểu của
    /// Result) nên không trả thẳng <c>ToActionResult&lt;T&gt;</c> được. CÙNG hàm <c>Problem</c> với hai overload trên: hai
    /// chỗ dựng ProblemDetails là hai chỗ lệch nhau.
    ///
    /// Kiểu trả về là <see cref="ActionResult"/> chứ không còn <c>ObjectResult</c> (Q-D4): nhánh <c>errors</c> dùng
    /// <c>ValidationProblem</c>, thứ trả <c>BadRequestObjectResult</c> qua đường khác. Ba chỗ gọi của <c>AuthController</c>
    /// trả vào <c>ActionResult&lt;T&gt;</c> nên vẫn compile — <c>ActionResult</c> có toán tử chuyển ngầm sang đó.
    /// </summary>
    public static ActionResult ToActionResult(this Error error, ControllerBase controller) => Problem(controller, error);

    /// <summary>
    /// <see cref="Error.Title"/> null thì <c>SharedKernelProblemDetailsFactory</c> điền title mặc định theo status — factory mặc
    /// định của MVC để 410/423 KHÔNG có title, trái <c>required</c> của hợp đồng (D9).
    ///
    /// Nhánh <c>errors</c> (Q-D4) đi qua <c>ValidationProblem(ModelStateDictionary)</c> chứ không tự dựng
    /// <c>ValidationProblemDetails</c>: chỉ đường đó mới ghé <c>SharedKernelProblemDetailsFactory.CreateValidationProblemDetails</c>
    /// → <c>ValidationErrors.From</c>, tức là cùng một bộ lọc key/thông điệp với 400 tự động của [ApiController].
    /// </summary>
    private static ActionResult Problem(ControllerBase controller, Error error)
    {
        if (error.Errors is { } errors)
        {
            var modelState = new ModelStateDictionary();
            foreach (var (field, messages) in errors)
                foreach (var message in messages)
                    modelState.AddModelError(field, message);

            return controller.ValidationProblem(modelState);
        }

        return controller.Problem(statusCode: error.Status, detail: error.Message, title: error.Title);
    }
}
