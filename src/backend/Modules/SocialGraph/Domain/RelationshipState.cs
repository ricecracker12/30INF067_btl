namespace SocialApp.Modules.SocialGraph.Domain;

/// <summary>
/// Quan hệ bạn bè nhìn từ một phía — trùng tên chuỗi
/// <c>"none" | "outgoing" | "incoming" | "friends"</c> của hợp đồng (Đ-4.16).
/// Ánh xạ sang DTO là việc của tầng Presentation/Application, không của Domain (L1).
/// </summary>
public enum FriendshipView
{
    None,
    Outgoing,
    Incoming,
    Friends,
}

/// <summary>
/// Hàm thuần chuyển dòng <see cref="Friendship"/> thành mức nhìn từ phía actor —
/// cùng nếp <c>PostVisibility</c> / <c>PostContentPolicy</c>.
/// </summary>
public static class RelationshipState
{
    /// <summary>Quan hệ nhìn từ phía <paramref name="actorId"/>. Dòng null = chưa có quan hệ.</summary>
    public static FriendshipView Of(Friendship? friendship, Guid actorId)
    {
        if (friendship is null)
            return FriendshipView.None;

        // So thẳng hai cột, không dựng FriendPair: dòng đọc từ DB đã chuẩn hóa sẵn, và FriendPair chỉ tạo được qua Of.
        if (actorId != friendship.UserMinId && actorId != friendship.UserMaxId)
            throw new ArgumentException(
                "actorId không thuộc cặp quan hệ của dòng friendships này.",
                nameof(actorId));

        if (friendship.Status == FriendshipStatus.Accepted)
            return FriendshipView.Friends;

        return friendship.RequesterId == actorId
            ? FriendshipView.Outgoing
            : FriendshipView.Incoming;
    }
}
