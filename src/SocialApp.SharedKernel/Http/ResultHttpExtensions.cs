using Microsoft.AspNetCore.Mvc;
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
/// Mỗi endpoint như vậy PHẢI có dòng trong AuthZMatrix.cs (AGENTS.md Mục 10).
/// </summary>
public static class ResultHttpExtensions
{
    public static IActionResult ToActionResult(this Result result, ControllerBase controller) =>
        result.IsSuccess ? controller.NoContent() : Problem(controller, result.Error!.Value);

    public static ActionResult<T> ToActionResult<T>(this Result<T> result, ControllerBase controller) =>
        result.IsSuccess ? controller.Ok(result.Value) : Problem(controller, result.Error!.Value);

    private static ObjectResult Problem(ControllerBase controller, Error error) =>
        controller.Problem(statusCode: error.Status, detail: error.Message);
}
