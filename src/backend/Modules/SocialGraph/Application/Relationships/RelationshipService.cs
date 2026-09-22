using SocialApp.Modules.SocialGraph.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Results;
using SocialApp.SharedKernel.Storage;

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
    TimeProvider clock,
    IObjectStorage storage)
{
    private readonly IRelationshipStore _store = store;
    private readonly IUserDirectory _directory = directory;
    private readonly IFeedSourceCache _feedSources = feedSources;
    private readonly SocialGraphEvents _events = events;
    private readonly TimeProvider _clock = clock;
    private readonly IObjectStorage _storage = storage;

    /// <summary>
    /// Token cho việc xóa cache SAU <c>COMMIT</c>: không hủy được. Client ngắt kết nối lúc này thì thay đổi đã nằm trong
    /// DB — truyền token của request vào là bỏ qua xóa cache, hai người đọc nguồn cũ thêm 60s. Cùng lý do
    /// <see cref="IFeedSourceCache"/> nuốt lỗi Redis thay vì ném.
    /// </summary>
    private static readonly CancellationToken PostCommit = CancellationToken.None;

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

        await _feedSources.InvalidateAsync(actorId, request.UserId, PostCommit);
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

        await _feedSources.InvalidateAsync(actorId, userId, PostCommit);
        _events.FriendRequestAccepted(userId, actorId);

        var following = await _store.IsFollowingAsync(actorId, userId, ct);
        return new RelationshipResponse(userId, FriendshipView.Friends, following);
    }

    /// <summary>
    /// <c>DELETE /friends/requests/{userId}</c> — hủy lời mình gửi, hoặc từ chối lời nhận được (FR-011).
    /// Một endpoint, chiều nào cũng xóa. Không mã quyền: <c>[Authorize]</c> đã xong ở controller (Đ-4.12).
    ///
    /// <list type="number">
    /// <item>Khác mình → 400, <b>trước</b> DB. <see cref="FriendPair.Of"/> ném nếu lọt.</item>
    /// <item>Không tra hồ sơ: hợp đồng không có 404. Không có lời mời vẫn 204.</item>
    /// <item><c>DELETE</c> cặp + <c>pending</c>. Quan hệ <c>accepted</c> không khớp vế trạng thái.</item>
    /// <item><c>changed &gt; 0</c> mới xóa cache nguồn cả hai phía, sau <c>COMMIT</c>. Không có event (Đ-4.15 chỉ
    /// <c>FriendRequestSent</c> / <c>FriendRequestAccepted</c>).</item>
    /// </list>
    /// </summary>
    public async Task<Result> DeclineOrCancelAsync(Guid actorId, Guid userId, CancellationToken ct)
    {
        if (actorId == userId)
            return SocialGraphErrors.SelfDecline;

        var pair = FriendPair.Of(actorId, userId);
        if (await _store.DeletePendingAsync(pair, ct))
            await _feedSources.InvalidateAsync(actorId, userId, PostCommit);

        return Result.Success();
    }

    /// <summary>
    /// <c>DELETE /friends/{userId}</c> — hủy kết bạn (FR-011). <c>[Authorize]</c> trần (Đ-4.12): người bị gỡ quyền
    /// kết bạn vẫn hủy được. Lời mời <c>pending</c> và dòng <c>follows</c> không bị đụng (Đ-4.5).
    ///
    /// Khác mình → 400 trước DB. 0 dòng vẫn 204. Chỉ xóa cache nguồn cả hai phía khi có dòng <c>accepted</c> bị xóa,
    /// sau <c>COMMIT</c> — request feed kế tiếp của hai người dùng nguồn mới (Mục 7.3).
    /// </summary>
    public async Task<Result> UnfriendAsync(Guid actorId, Guid userId, CancellationToken ct)
    {
        if (actorId == userId)
            return SocialGraphErrors.SelfUnfriend;

        var pair = FriendPair.Of(actorId, userId);
        if (await _store.DeleteAcceptedAsync(pair, ct))
            await _feedSources.InvalidateAsync(actorId, userId, PostCommit);

        return Result.Success();
    }

    /// <summary>
    /// <c>GET /friends</c> — bạn của chính người gọi, mới kết bạn trước. Không 404: chưa có bạn là trang rỗng.
    /// </summary>
    public async Task<FriendPage> ListFriendsAsync(
        Guid actorId, string? rawCursor, int limit, CancellationToken ct)
    {
        FriendCursor? cursor = FriendCursor.TryDecode(rawCursor, out var decoded) ? decoded : null;
        var rows = await _store.ListFriendsAsync(actorId, cursor, limit + 1, ct);
        var (items, next) = await ToCardsAsync(rows, limit, ct);
        return new FriendPage(items, next);
    }

    /// <summary>
    /// <c>GET /friends/requests</c>. <paramref name="incoming"/> đã được validator chặn giá trị lạ.
    /// Không gửi <c>direction</c> thì controller truyền <c>true</c> (mặc định incoming).
    /// </summary>
    public async Task<FriendRequestPage> ListRequestsAsync(
        Guid actorId, bool incoming, string? rawCursor, int limit, CancellationToken ct)
    {
        FriendCursor? cursor = FriendCursor.TryDecode(rawCursor, out var decoded) ? decoded : null;
        var rows = await _store.ListRequestsAsync(actorId, incoming, cursor, limit + 1, ct);
        var (items, next) = await ToCardsAsync(rows, limit, ct);
        return new FriendRequestPage(items, next);
    }

    /// <summary>
    /// <c>PUT /follows/{userId}</c> — FR-012. Theo dõi không cần người kia đồng ý và không tạo lời mời kết bạn (Đ-4.5).
    ///
    /// <list type="number">
    /// <item>Tầng 2 — quyền <c>friend.request</c>: đã xong ở controller (Đ-4.12).</item>
    /// <item>Khác mình → 400, <b>trước</b> DB. CHECK <c>ck_follows_not_self</c> thành 500 nếu lọt.</item>
    /// <item>Người được theo dõi có hồ sơ (Đ-2.4) → 404.</item>
    /// <item><c>INSERT … ON CONFLICT DO NOTHING</c>. Đã theo dõi rồi vẫn 204, đúng một dòng.</item>
    /// <item><c>inserted == 1</c> mới xóa cache nguồn của <b>người theo dõi</b>, sau <c>COMMIT</c>. Người được theo dõi
    /// không đổi nguồn feed của họ. Không event (Đ-4.15 chỉ lời mời kết bạn).</item>
    /// </list>
    /// </summary>
    public async Task<Result> FollowAsync(Guid actorId, Guid userId, CancellationToken ct)
    {
        if (actorId == userId)
            return SocialGraphErrors.SelfFollow;

        var cards = await _directory.GetManyAsync([userId], ct);
        if (!cards.ContainsKey(userId))
            return SocialGraphErrors.UserNotFound;

        if (await _store.AddFollowAsync(actorId, userId, _clock.GetUtcNow(), ct))
            await _feedSources.InvalidateAsync(actorId, ct: PostCommit);

        return Result.Success();
    }

    /// <summary>
    /// <c>DELETE /follows/{userId}</c> — bỏ theo dõi (FR-012). <c>[Authorize]</c> trần (Đ-4.12).
    /// 0 dòng vẫn 204. Quan hệ bạn bè không bị đụng (Đ-4.5).
    ///
    /// Chỉ xóa cache nguồn của người theo dõi khi có dòng bị xóa, sau <c>COMMIT</c>. Không kiểm hồ sơ: yaml không có 404.
    /// Chính mình không có dòng nào để xóa (CHECK chặn từ lúc tạo) nên vẫn 204 — không gọi <see cref="FriendPair.Of"/>.
    /// </summary>
    public async Task<Result> UnfollowAsync(Guid actorId, Guid userId, CancellationToken ct)
    {
        if (await _store.DeleteFollowAsync(actorId, userId, ct))
            await _feedSources.InvalidateAsync(actorId, ct: PostCommit);

        return Result.Success();
    }

    /// <summary>
    /// Một <see cref="IUserDirectory.GetManyAsync"/> cho cửa sổ <paramref name="limit"/> dòng gốc.
    /// Thiếu hồ sơ thì thẻ vắng mặt. <c>nextCursor</c> lấy từ dòng thứ <paramref name="limit"/> của danh sách
    /// gốc, trước khi lọc — không phải thẻ cuối còn lại (Đ-4.9). Hết dữ liệu khi không có dòng thừa.
    /// </summary>
    private async Task<(IReadOnlyList<FriendCard> Items, string? NextCursor)> ToCardsAsync(
        IReadOnlyList<FriendListRow> rows, int limit, CancellationToken ct)
    {
        var window = rows.Count > limit ? rows.Take(limit).ToList() : rows;
        if (window.Count == 0)
            return ([], null);

        var cards = await _directory.GetManyAsync(window.Select(r => r.OtherUserId).ToArray(), ct);

        var items = new List<FriendCard>(window.Count);
        foreach (var row in window)
        {
            if (!cards.TryGetValue(row.OtherUserId, out var card))
                continue;

            items.Add(new FriendCard(
                new FriendUser(
                    card.UserId,
                    card.DisplayName,
                    card.AvatarKey is { } key ? _storage.CreatePresignedGet(key) : null),
                row.Since));
        }

        var next = rows.Count > limit
            ? new FriendCursor(window[^1].Since, window[^1].OtherUserId).Encode()
            : null;
        return (items, next);
    }
}
