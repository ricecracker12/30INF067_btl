namespace SocialApp.Modules.SocialGraph.Application.Relationships;

/// <summary>
/// Thẻ người trên danh sách bạn / lời mời. Khác <see cref="SocialApp.SharedKernel.Contracts.UserCard"/>: cái đó mang
/// <c>AvatarKey</c>, cái này mang URL đã ký. Tên trường khớp schema <c>UserCard</c> của <c>socialgraph-v1.yaml</c>.
/// </summary>
public sealed record FriendUser(Guid UserId, string DisplayName, string? AvatarUrl);

/// <summary>
/// Một thẻ. <paramref name="Since"/> là lúc trở thành bạn hoặc lúc gửi lời mời, tùy danh sách.
/// </summary>
public sealed record FriendCard(FriendUser User, DateTimeOffset Since);

/// <summary>Body 200 của <c>GET /friends</c>. <paramref name="NextCursor"/> <c>null</c> khi hết, không phải <c>""</c>.</summary>
public sealed record FriendPage(IReadOnlyList<FriendCard> Items, string? NextCursor);

/// <summary>Body 200 của <c>GET /friends/requests</c>. Cùng hình dạng <see cref="FriendPage"/>, schema riêng trên hợp đồng.</summary>
public sealed record FriendRequestPage(IReadOnlyList<FriendCard> Items, string? NextCursor);
