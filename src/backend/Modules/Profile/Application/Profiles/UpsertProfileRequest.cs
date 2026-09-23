using System.ComponentModel.DataAnnotations;

namespace SocialApp.Modules.Profile.Application.Profiles;

/// <summary>
/// Body của <c>PUT /users/me/profile</c>, khớp schema <c>UpsertProfileRequest</c> của <c>profile-v1.yaml</c>.
///
/// Class <c>init</c> chứ không phải positional record, và KHÔNG dùng từ khóa C# <c>required</c> — chép nguyên lý do đã
/// ghi ở <c>RegisterRequest</c> của GĐ1 (thi công D9): với <c>required</c>, System.Text.Json chặn body thiếu trường
/// TRƯỚC FluentValidation, nên người dùng nhận một lỗi JSON chung thay vì câu tiếng Việt của validator. Thiếu trường thì
/// <see cref="DisplayName"/> là chuỗi rỗng và validator bắt.
///
/// <c>[Required]</c> CHỈ để Swagger ghi <c>required: [displayName]</c> cho cổng hợp đồng (B4) — DataAnnotations
/// validation đã tắt ở host. Validate thật do <see cref="UpsertProfileRequestValidator"/>.
///
/// <see cref="Bio"/> KHÔNG có <c>[Required]</c>: hợp đồng để nó ngoài <c>required</c>, và Q-D3 đã chốt vắng mặt hay
/// <c>null</c> đều nghĩa là XÓA bio (PUT thay thế toàn phần) — hai trường hợp đó về tới đây đều là <c>null</c>.
///
/// Field lạ trong body → 400 nhờ <c>UnmappedMemberHandling = Disallow</c> ở host, không phải nhờ gì trong lớp này.
/// </summary>
public sealed class UpsertProfileRequest
{
    [Required]
    public string DisplayName { get; init; } = "";

    public string? Bio { get; init; }
}
