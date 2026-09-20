using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialApp.Modules.Profile.Application.Profiles;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Http;

namespace SocialApp.Modules.Profile.Presentation;

/// <summary>
/// Tầng HTTP của module Profile — bốn action theo <c>profile-v1.yaml</c>: <c>GET {userId}/profile</c> (D1),
/// <c>PUT me/profile</c> (D2), <c>PUT me/avatar</c> + <c>DELETE me/avatar</c> (D3). Đủ bốn.
///
/// <c>GET {userId}/profile</c> và <c>PUT me/profile</c> KHÔNG tranh route nhau dù <c>me</c> khớp được template
/// <c>{userId}</c>: khác HTTP method. Hệ quả có thật: <c>GET /users/me/profile</c> rơi vào <c>{userId}</c> → 400, còn
/// <c>PUT /api/v1/users/{someGuid}/profile</c> → 405. Cả hai đều đúng hợp đồng (không có operation nào như vậy).
///
/// <list type="bullet">
/// <item><b>Route KHÔNG có ràng buộc <c>:guid</c>.</b> Với <c>Guid userId</c>, id sai dạng làm model binding hỏng và
/// <c>[ApiController]</c> trả 400 kèm <c>errors.userId</c> — đúng hợp đồng. Thêm <c>{userId:guid}</c> thì route không khớp
/// nữa và ra 404: sai hợp đồng, và tệ hơn là 404 đó trùng mã với tín hiệu onboarding nên FE đọc nhầm thành "chưa có hồ sơ".
/// Hệ quả kèm theo: <c>GET /users/me/profile</c> (không có trong hợp đồng) rơi vào <c>{userId}</c> → 400, không phải một
/// endpoint ẩn.</item>
/// <item><b>Không <c>[RequirePermission]</c>.</b> Hồ sơ công khai trong MVP (Mục 6.1) — chỉ cần <c>[Authorize]</c>. Không
/// mã nào trong 17 quyền nghĩa là "đọc hồ sơ người khác" và bịa thêm quyền là sửa ma trận (Đ-2.6).</item>
/// <item><b>Không <c>[Produces("application/json")]</c> ở class.</b> Bài học <c>MeController</c> của GĐ1: nó đè
/// <c>application/problem+json</c> của nhánh lỗi, và ở đây nhánh lỗi 404 chính là thứ FE dựa vào.</item>
/// <item><b>Không <c>[EnableRateLimiting("auth")]</c>:</b> khối D dùng hạn mức chung 100 req/phút theo user.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize]
[ApiExplorerSettings(GroupName = ProfileApiGroup.Name)]
public sealed class ProfilesController(ProfileService profiles) : ControllerBase
{
    /// <summary>
    /// Hồ sơ công khai của một người dùng. 404 = chưa onboarding (Đ-2.4) hoặc không tồn tại — cùng một phản hồi, và
    /// <c>detail</c> không nêu <paramref name="userId"/> (PROF-03).
    /// </summary>
    [HttpGet("{userId}/profile")]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ProfileResponse>> Get(Guid userId, CancellationToken ct)
    {
        var result = await profiles.GetAsync(userId, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Onboarding (Đ-2.4) hoặc sửa hồ sơ của chính mình. Upsert: lần đầu TẠO, các lần sau SỬA, cùng mã <b>200</b> —
    /// hợp đồng cố ý không có 201, FE không phải phân nhánh.
    ///
    /// <c>actorId</c> lấy từ <c>User.GetUserId()</c>, KHÔNG từ route/body/query (Mục 1.3 luật 5). Đây là lý do route là
    /// <c>me</c> chứ không phải <c>{userId}</c>: không có id nào của người khác để truyền vào, nên không có tầng 3 để
    /// quên. Trả nguyên <c>ProfileResponse</c> để FE không phải gọi lại <c>GET</c>.
    ///
    /// Không có 404 ở đây: chưa có hồ sơ thì câu lệnh TẠO nó. Đó là cả điểm của upsert.
    /// </summary>
    [HttpPut("me/profile")]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<ProfileResponse>> Upsert(UpsertProfileRequest request, CancellationToken ct)
    {
        var result = await profiles.UpsertAsync(User.GetUserId(), request, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Đặt ảnh đại diện từ một object ĐÃ nằm trên R2 (Đ-2.5: byte ảnh không đi qua API). Nhận <c>mediaKey</c>, không
    /// nhận URL — xem <see cref="SetAvatarRequest"/>.
    ///
    /// Ba mã lỗi ứng với ba lớp của Đ-2.8, thứ tự và lý do ở <see cref="ProfileService.SetAvatarAsync"/>: <b>400</b> sai
    /// dạng hoặc object chưa có/sai loại, <b>403</b> key của người khác (Đ-2.7) hoặc người gọi chưa có hồ sơ (Q-D9).
    /// </summary>
    [HttpPut("me/avatar")]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<ActionResult<ProfileResponse>> SetAvatar(SetAvatarRequest request, CancellationToken ct)
    {
        var result = await profiles.SetAvatarAsync(User.GetUserId(), request.MediaKey, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Gỡ ảnh đại diện — <c>avatar_key = NULL</c>, <b>không</b> xóa object trên R2 (Đ-2.10).
    ///
    /// Chỉ có 204 và 401: idempotent theo hợp đồng, nên "vốn không có avatar" và "chưa có hồ sơ" đều là 204.
    /// <c>ToActionResult</c> của <c>Result</c> không mang dữ liệu trả thẳng <c>NoContent()</c>.
    /// </summary>
    [HttpDelete("me/avatar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> RemoveAvatar(CancellationToken ct)
    {
        var result = await profiles.RemoveAvatarAsync(User.GetUserId(), ct);
        return result.ToActionResult(this);
    }
}
