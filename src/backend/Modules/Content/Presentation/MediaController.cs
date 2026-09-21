using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Content.Application;
using SocialApp.Modules.Content.Application.Media;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Http;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Content.Presentation;

/// <summary>
/// Tầng HTTP cho <c>POST /media/uploads</c> (D4). Một action, và tách khỏi <c>PostsController</c> vì tiền tố route khác
/// (<c>/media</c> so với <c>/posts</c>) — cùng nhóm Swagger <c>content-v1</c>, vì nhóm bám theo MODULE
/// (<see cref="ContentApiGroup"/>).
///
/// <list type="bullet">
/// <item><b>Không <c>[Produces("application/json")]</c> ở class</b> — bài học <c>MeController</c> của GĐ1: nó đè
/// <c>application/problem+json</c> của nhánh lỗi.</item>
/// <item><b>Không <c>[EnableRateLimiting("auth")]</c></b>: khối D dùng hạn mức chung 100 req/phút theo user. Presign
/// theo lô tồn tại chính để một bài 10 ảnh tốn MỘT lượt trong hạn mức đó (Đ-2.15).</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/v1/media")]
[Authorize]
[ApiExplorerSettings(GroupName = ContentApiGroup.Name)]
public sealed class MediaController(UploadTicketService tickets) : ControllerBase
{
    /// <summary>
    /// Xin URL đã ký cho tối đa 10 file. Trả <b>201</b> với mảng ticket cùng thứ tự <c>files</c>; không tạo dòng nào
    /// trong DB, không gọi mạng.
    ///
    /// <b>Hai mức quyền theo <c>purpose</c> (Đ-2.6, chốt Q-D5).</b> <c>[Authorize]</c> ở class lo mức 1 cho mọi
    /// <c>purpose</c>; <c>purpose=post</c> đi thêm qua <see cref="IAuthorizationService"/> với CHÍNH policy mà
    /// <c>[RequirePermission("post.create")]</c> dựng. Ba thứ đi kèm miễn phí vì đó là cùng một đường:
    /// <see cref="PermissionHandler"/>, cache quyền, và short-circuit ADMIN.
    ///
    /// Vì sao KHÔNG gọi thẳng <c>IPermissionCache</c> như Đ-2.6 viết ban đầu: hỏi cache với vai trò Admin trả RỖNG — Admin
    /// không có dòng <c>role_permissions</c> (<c>RBAC-01</c>), lối tắt nằm trong handler — nên Admin sẽ không xin được URL
    /// tải ảnh bài. Cách vá là lặp <c>if role == ADMIN</c> ngoài handler, đúng thứ Mục 3.2 của GĐ1 cấm.
    ///
    /// Vì sao ở CONTROLLER chứ không ở service: kiểm này cần <see cref="ControllerBase.User"/> — từ vựng của HTTP.
    /// <see cref="UploadTicketService"/> chỉ nhận <c>actorId</c> và <c>purpose</c>.
    ///
    /// Vì sao KHÔNG đặt <c>[RequirePermission(ContentPermissions.PostCreate)]</c> lên cả action: người chưa có quyền đăng
    /// bài vẫn phải đổi được ảnh đại diện (Đ-2.6 nói thẳng). Hệ quả là mã quyền ở đây đi qua thân action nên
    /// <c>PermissionCodeUsageTests</c> (đọc attribute) không thấy — <c>ContentPermissionsTests</c> canh thay.
    /// </summary>
    [HttpPost("uploads")]
    [ProducesResponseType<IReadOnlyList<UploadTicket>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<UploadTicket>>> Create(
        CreateUploadsRequest request,
        [FromServices] IAuthorizationService authorization)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorization);

        if (request.Purpose == UploadPurpose.Post)
        {
            var check = await authorization.AuthorizeAsync(
                User, null, RequirePermissionAttribute.PolicyPrefix + ContentPermissions.PostCreate);
            if (!check.Succeeded)
                return Error.Forbidden.ToActionResult(this);
        }

        // Purpose chắc chắn có giá trị: auto-validation của FluentValidation chạy TRƯỚC action, và
        // CreateUploadsRequestValidator có NotNull() trên trường này (Q-D2). Không có action nào thấy request chưa hợp lệ.
        var created = tickets.Create(User.GetUserId(), request.Purpose!.Value, request.Files);

        // 201 như Register của GĐ1. KHÔNG CreatedAtAction: không có GET nào cho một ticket — nó không phải tài nguyên,
        // nó là một quyền ghi có hạn 10 phút.
        return StatusCode(StatusCodes.Status201Created, created);
    }
}
