using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Content.Application;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Content.Presentation;

/// <summary>
/// Tầng HTTP của bài đăng. <c>[Route("api/v1")]</c> trần chứ không <c>api/v1/posts</c>: module này còn sở hữu
/// <c>GET /users/{userId}/posts</c> (D6) — bài của một người là tài nguyên của Content, không của Profile, dù đường dẫn
/// bắt đầu bằng <c>/users</c>. Mỗi action tự khai phần đuôi của mình.
///
/// Cùng nhóm Swagger <c>content-v1</c> với <see cref="MediaController"/>: nhóm bám theo MODULE, không theo controller.
///
/// Không <c>[Produces("application/json")]</c> ở class (bài học <c>MeController</c> của GĐ1: nó đè
/// <c>application/problem+json</c> của nhánh lỗi), không <c>[EnableRateLimiting("auth")]</c> (khối D dùng hạn mức chung
/// 100 req/phút theo user).
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
[ApiExplorerSettings(GroupName = ContentApiGroup.Name)]
public sealed class PostsController(PostService posts) : ControllerBase
{
    /// <summary>
    /// Đăng bài (FR-004, BR-01). Thứ tự sáu bước kiểm nằm ở <see cref="PostService.CreateAsync"/> và là một phần của
    /// hợp đồng — controller chỉ lấy <c>actorId</c> từ token rồi chuyển xuống.
    ///
    /// <c>[RequirePermission]</c> chứ không <c>[Authorize]</c> trần: đây là tầng 2 của Đ-2.6, và khác
    /// <c>POST /media/uploads</c> ở chỗ endpoint này chỉ có MỘT mức quyền nên khai được bằng attribute (Q-D5 chỉ áp cho
    /// endpoint hai mức). Nhờ vậy <c>PermissionCodeUsageTests</c> đọc được mã quyền ở đây.
    ///
    /// <c>actorId</c> từ <c>User.GetUserId()</c>, KHÔNG từ body (Mục 1.3 luật 5) — <c>CreatePostRequest</c> cố ý không
    /// có trường nào mang id, nên không có gì để tin nhầm.
    /// </summary>
    [HttpPost("posts")]
    [RequirePermission(ContentPermissions.PostCreate)]
    [ProducesResponseType<PostResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<PostResponse>> Create(CreatePostRequest request, CancellationToken ct)
    {
        var result = await posts.CreateAsync(User.GetUserId(), request, ct);

        // ToActionResult trả 200 khi thành công — đăng bài là 201. Không CreatedAtAction: GET /posts/{id} chưa tồn tại
        // ở commit này (D6), và header Location không nằm trong hợp đồng.
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : result.ToActionResult(this);
    }
}
