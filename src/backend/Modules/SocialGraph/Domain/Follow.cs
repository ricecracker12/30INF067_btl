namespace SocialApp.Modules.SocialGraph.Domain;

/// <summary>
/// Theo dõi một chiều (bảng <c>socialgraph.follows</c>). Không <c>UpdatedAt</c> — theo dõi
/// không sửa, chỉ tạo và xóa (Mục 4). Không navigation sang User/Profile (Đ-2.2).
/// </summary>
public sealed class Follow
{
    public Guid FollowerId { get; init; }

    public Guid FolloweeId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Tạo dòng theo dõi. Tự theo dõi bị chặn ở tầng D trước khi tới đây (CHECK DB canh lại).</summary>
    public static Follow Create(Guid followerId, Guid followeeId, DateTimeOffset now)
    {
        if (followerId == followeeId)
            throw new ArgumentException("Không thể theo dõi chính mình.", nameof(followeeId));

        return new Follow
        {
            FollowerId = followerId,
            FolloweeId = followeeId,
            CreatedAt = now,
        };
    }
}
