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
    private readonly IUserDirectory _directory = directory;
    private readonly IFeedSourceCache _feedSources = feedSources;
    private readonly SocialGraphEvents _events = events;
    private readonly TimeProvider _clock = clock;

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

    /// <summary>
    /// <c>POST /friends/requests</c> — FR-010. Thứ tự kiểm là một phần của hợp đồng (Đ-4.14, yaml):
    /// <list type="number">
    /// <item>Tầng 2 — quyền <c>friend.request</c>: đã xong ở controller.</item>
    /// <item>Khác mình → 400, <b>trước</b> DB. <see cref="FriendPair.Of"/> ném nếu lọt; CHECK DB thành 500.</item>
    /// <item>Người được mời có hồ sơ (Đ-2.4) → 404. Tra <see cref="IUserDirectory"/>, không SELECT friendships.</item>
    /// <item>INSERT trần; PK <c>PK_friendships</c> → 409. Không SELECT trước: hai đường tới 409 làm
    /// <c>FRD-06</c> mất tính tất định.</item>
    /// <item>Sau <c>COMMIT</c>: xóa cache nguồn cả hai phía, rồi <see cref="SocialGraphEvents.FriendRequestSent"/>.</item>
    /// </list>
    /// </summary>
    public async Task<Result<RelationshipResponse>> SendRequestAsync(
        Guid actorId, CreateFriendRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (actorId == request.UserId)
            return SocialGraphErrors.SelfRequest;

        var cards = await _directory.GetManyAsync([request.UserId], ct);
        if (!cards.ContainsKey(request.UserId))
            return SocialGraphErrors.UserNotFound;

        var friendship = Friendship.Request(actorId, request.UserId, _clock.GetUtcNow());
        if (!await _store.AddRequestAsync(friendship, ct))
            return SocialGraphErrors.RelationshipExists;

        await _feedSources.InvalidateAsync(actorId, request.UserId, ct);
        _events.FriendRequestSent(actorId, request.UserId);

        var following = await _store.IsFollowingAsync(actorId, request.UserId, ct);
        return new RelationshipResponse(request.UserId, FriendshipView.Outgoing, following);
    }

    /// <summary>
    /// <c>POST /friends/requests/{userId}/accept</c> — FR-011. Một câu <c>UPDATE</c> có điều kiện
    /// (Đ-4.14), không cửa sổ race giữa SELECT và UPDATE.
    ///
    /// <list type="number">
    /// <item>Tầng 2 — quyền <c>friend.respond</c>: đã xong ở controller.</item>
    /// <item>Khác mình → 400, <b>trước</b> DB. <see cref="FriendPair.Of"/> ném nếu lọt.</item>
    /// <item>Không tra hồ sơ: hợp đồng không có 404. Người gọi chưa onboarding vẫn 403 nếu 0 dòng
    /// (cạm bẫy B2 — đừng biến <c>TC-A03-friend-accept</c> thành xanh vì lý do sai).</item>
    /// <item><c>UPDATE</c> cặp + <c>pending</c> + <c>requester_id = userId</c>. 0 dòng → 403 cùng
    /// <see cref="Result{T}.Forbidden()"/> cho mọi lý do (quy ước 3b).</item>
    /// <item>Sau <c>COMMIT</c>: xóa cache nguồn cả hai phía, rồi
    /// <see cref="SocialGraphEvents.FriendRequestAccepted"/>.</item>
    /// </list>
    /// </summary>
    public async Task<Result<RelationshipResponse>> AcceptRequestAsync(
        Guid actorId, Guid userId, CancellationToken ct)
    {
        if (actorId == userId)
            return SocialGraphErrors.SelfAccept;

        var pair = FriendPair.Of(actorId, userId);
        if (!await _store.AcceptIncomingAsync(pair, userId, _clock.GetUtcNow(), ct))
            return Result<RelationshipResponse>.Forbidden();

        await _feedSources.InvalidateAsync(actorId, userId, ct);
        _events.FriendRequestAccepted(userId, actorId);

        var following = await _store.IsFollowingAsync(actorId, userId, ct);
        return new RelationshipResponse(userId, FriendshipView.Friends, following);
    }
}
