namespace SocialApp.Modules.SocialGraph.Domain;

/// <summary>
/// Quan hệ bạn bè (ENT-04, bảng <c>socialgraph.friendships</c>). PK cặp chuẩn hóa
/// <c>(user_min_id, user_max_id)</c> chính là BR-03 — không navigation sang User/Profile (Đ-2.2).
/// </summary>
public sealed class Friendship
{
    public Guid UserMinId { get; init; }

    public Guid UserMaxId { get; init; }

    /// <summary>Người gửi lời mời — phải ∈ cặp; CHECK ở configuration.</summary>
    public Guid RequesterId { get; init; }

    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Đi cùng <see cref="FriendshipStatus.Accepted"/> — CHECK ở configuration.</summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>
    /// Tạo lời mời pending. Chuẩn hóa cặp qua <see cref="FriendPair.Of"/> — chỗ duy nhất
    /// đảm bảo thứ tự khớp uuid Postgres trước khi INSERT.
    /// </summary>
    public static Friendship Request(Guid requester, Guid recipient, DateTimeOffset now)
    {
        var pair = FriendPair.Of(requester, recipient);
        return new Friendship
        {
            UserMinId = pair.Min,
            UserMaxId = pair.Max,
            RequesterId = requester,
            Status = FriendshipStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
