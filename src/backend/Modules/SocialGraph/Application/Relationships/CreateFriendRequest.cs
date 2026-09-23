using System.ComponentModel.DataAnnotations;

namespace SocialApp.Modules.SocialGraph.Application.Relationships;

/// <summary>
/// Body của <c>POST /friends/requests</c>, khớp schema <c>CreateFriendRequest</c> của
/// <c>socialgraph-v1.yaml</c>. Chỉ MỘT id — người kia. Người gửi luôn là người gọi (Mục 6.2),
/// không nhận từ body.
///
/// Class <c>init</c> chứ không positional record, và KHÔNG dùng từ khóa C# <c>required</c>: thiếu
/// <c>userId</c> phải tới FluentValidation (câu tiếng Việt), không bị System.Text.Json chặn trước.
/// <c>[Required]</c> chỉ để Swagger ghi <c>required: [userId]</c> — DataAnnotations đã tắt ở host.
/// </summary>
public sealed class CreateFriendRequest
{
    [Required]
    public Guid UserId { get; init; }
}
