using SocialApp.Modules.SocialGraph.Domain;

namespace SocialApp.Modules.SocialGraph.Application.Relationships;

/// <summary>
/// Body 200/201 của mọi thao tác quan hệ có body — khớp schema <c>RelationshipResponse</c> của
/// <c>socialgraph-v1.yaml</c>. FE vẽ lại nút từ phản hồi này, không gọi thêm <c>GET</c> (Mục 7.1).
///
/// <see cref="Friendship"/> ghi ra chữ thường (<c>none</c> / <c>outgoing</c> / <c>incoming</c> / <c>friends</c>) nhờ
/// <c>JsonStringEnumConverter(CamelCase)</c> của host — không gắn converter riêng trên enum (cạm bẫy D0).
/// <see cref="Following"/> độc lập với <see cref="Friendship"/> (Đ-4.5).
/// </summary>
/// <param name="UserId">Người kia trong quan hệ, không phải người gọi.</param>
public sealed record RelationshipResponse(Guid UserId, FriendshipView Friendship, bool Following);
