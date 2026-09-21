using System.ComponentModel.DataAnnotations;

namespace SocialApp.Modules.Profile.Application.Profiles;

/// <summary>
/// Body của <c>PUT /users/me/avatar</c>, khớp schema <c>SetAvatarRequest</c> của <c>profile-v1.yaml</c>.
///
/// Class <c>init</c> + <c>[Required]</c> chỉ-để-Swagger, KHÔNG dùng từ khóa C# <c>required</c> — cùng lý do đã ghi ở
/// <see cref="UpsertProfileRequest"/>.
///
/// Nhận <b>key</b> chứ không nhận URL (Đ-2.9): client không bao giờ cầm URL đã ký của ai khác, và key thì kiểm được tiền
/// tố người gọi. Byte ảnh KHÔNG đi qua API (Đ-2.5) — trình duyệt <c>PUT</c> thẳng lên R2 trước, rồi mới gọi endpoint này.
/// </summary>
public sealed class SetAvatarRequest
{
    [Required]
    public string MediaKey { get; init; } = "";
}
