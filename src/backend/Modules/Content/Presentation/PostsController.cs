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
public sealed class PostsController(PostService posts, PostReadService reads) : ControllerBase
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

        // ToActionResult trả 200 khi thành công — đăng bài là 201. Không CreatedAtAction dù GET /posts/{id} đã có từ
        // D6: header Location không nằm trong hợp đồng, và thêm nó là thêm một thứ FE không đọc mà cổng B4 phải so.
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : result.ToActionResult(this);
    }

    /// <summary>
    /// Đọc một bài, BR-02 tại thời điểm đọc (Mục 7.4). Không tồn tại, đã xóa mềm, hay không được xem đều là <b>404</b>
    /// với cùng một body — trả 403 cho ca cuối là để status code tự tố cáo bài có tồn tại. Dòng <c>READ-01</c>.
    ///
    /// Route KHÔNG có ràng buộc <c>:guid</c> (Mục 1.5): với tham số <c>Guid postId</c>, id sai dạng làm model binding
    /// hỏng và <c>[ApiController]</c> trả 400 kèm <c>errors.postId</c> — đúng hợp đồng. Thêm <c>{postId:guid}</c> thì
    /// route không khớp và ra 404, trùng mã với "không tìm thấy bài" nên FE đọc nhầm hai chuyện thành một.
    /// </summary>
    [HttpGet("posts/{postId}")]
    [RequirePermission(ContentPermissions.PostReadPublic)]
    [ProducesResponseType<PostResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PostResponse>> Get(Guid postId, CancellationToken ct)
    {
        var result = await reads.GetAsync(postId, User.GetUserId(), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Sửa bài của mình (FR-005). Chỉ <c>body</c> và <c>privacy</c> — gửi <c>mediaKeys</c> vào đây là field lạ → 400,
    /// đúng ý vì GĐ2 không cho sửa ảnh (Mục 7.3).
    ///
    /// <b>Không có 404</b> trong danh sách mã: bài không tồn tại, của người khác, hay đã xóa mềm đều là <b>403</b> với
    /// cùng một body (quy ước 3b). Đây là dòng <c>TC-A03</c> của AuthZ matrix.
    /// </summary>
    [HttpPatch("posts/{postId}")]
    [RequirePermission(ContentPermissions.PostUpdate)]
    [ProducesResponseType<PostResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<ActionResult<PostResponse>> Update(Guid postId, UpdatePostRequest request, CancellationToken ct)
    {
        var result = await posts.UpdateAsync(postId, User.GetUserId(), request, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Bài của một người, mới nhất trước, phân trang keyset (Đ-2.11).
    ///
    /// <b>Không có 404</b> trong danh sách mã: người dùng không tồn tại, chưa có bài, hay có bài mà người gọi không được
    /// xem đều là <c>items: []</c> — hợp đồng ghi rõ. Bài không được xem đơn giản là không có trong danh sách, không 403.
    ///
    /// <paramref name="query"/> là một model <c>[FromQuery]</c> chứ không phải hai tham số rời, để FluentValidation
    /// auto-validation áp được: <c>cursor</c> rác và <c>limit</c> ngoài <c>1..50</c> thành 400 trước khi vào action.
    /// </summary>
    [HttpGet("users/{userId}/posts")]
    [RequirePermission(ContentPermissions.PostReadPublic)]
    [ProducesResponseType<PostPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<PostPage>> ListByUser(
        Guid userId, [FromQuery] ListUserPostsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await reads.ListByUserAsync(userId, User.GetUserId(), query.Cursor, query.EffectiveLimit, ct);
        return Ok(page);
    }
}
