using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.SocialGraph.Application.Relationships;

/// <summary>
/// Nghiệp vụ quan hệ (kết bạn, theo dõi, đọc trạng thái). Nhận <see cref="IRelationshipStore"/> chứ không nhận
/// <c>SocialGraphDbContext</c> — lớp này test được mà không cần Postgres.
///
/// <c>IFeedSourceCache</c> (C1) cắm vào constructor từ D0 — không đăng ký bản rỗng (cùng loại bẫy
/// <c>AlwaysStrangers</c>). D2–D6 gọi <c>InvalidateAsync</c> sau <c>COMMIT</c>.
/// </summary>
public sealed class RelationshipService(
    IRelationshipStore store,
    IUserDirectory directory,
    IFeedSourceCache feedSources,
    SocialGraphEvents events,
    TimeProvider clock)
{
    private readonly IRelationshipStore _store = store;
#pragma warning disable IDE0052 // D1 đọc store; D2–D6 đọc các trường này lần đầu.
    private readonly IUserDirectory _directory = directory;
    private readonly IFeedSourceCache _feedSources = feedSources;
    private readonly SocialGraphEvents _events = events;
    private readonly TimeProvider _clock = clock;
#pragma warning restore IDE0052

    /// <summary>
    /// <c>GET /relationships/{userId}</c> — trạng thái nút trên hồ sơ (Đ-4.16).
    ///
    /// Chính mình → 400 <see cref="SocialGraphErrors.SelfRelationship"/>, <b>trước</b> DB
    /// (<see cref="FriendPair.Of"/> ném nếu đi tiếp). Người không tồn tại → 200 <c>none</c>/<c>false</c>:
    /// không gọi <see cref="IUserDirectory"/>, không 404 — endpoint đọc quan hệ không phải máy dò tài khoản
    /// (Mục 8.1).
    /// </summary>
    public async Task<Result<RelationshipResponse>> GetAsync(Guid userId, Guid actorId, CancellationToken ct)
    {
        if (actorId == userId)
            return SocialGraphErrors.SelfRelationship;

        var pair = FriendPair.Of(actorId, userId);
        var friendship = await _store.FindFriendshipAsync(pair, ct);
        var following = await _store.IsFollowingAsync(actorId, userId, ct);

        return new RelationshipResponse(userId, RelationshipState.Of(friendship, actorId), following);
    }
}
