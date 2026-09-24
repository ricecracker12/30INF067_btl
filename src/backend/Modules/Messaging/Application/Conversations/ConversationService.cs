using SocialApp.Modules.Messaging.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Ids;
using SocialApp.SharedKernel.Results;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.Modules.Messaging.Application.Conversations;

/// <summary>
/// Nghiệp vụ đọc/mở hội thoại và biên nhận (D1–D4, D6). Gửi tin ở <see cref="MessageSendService"/>. Mọi đường chạm MỘT hội thoại
/// đi qua <see cref="ConversationAccess"/> (tầng 3, BR-06) — service không tự so <c>UserAId</c>.
///
/// <c>actorId</c> luôn đến từ token (REST: <c>User.GetUserId()</c>, hub: <c>Context.UserIdentifier</c>) — không bao giờ từ route,
/// body hay tham số hub (B.10 điều 1).
/// </summary>
public sealed class ConversationService(
    IConversationStore store,
    ConversationAccess access,
    IFriendshipReader friendships,
    IUserDirectory directory,
    IObjectStorage storage,
    IChatNotifier notifier,
    TimeProvider clock)
{
    /// <summary>Tên hiển thị khi hồ sơ người kia không còn (MVP không xóa hồ sơ; lưới để một dòng hỏng không thành 500).</summary>
    public const string UnknownPeerName = "Người dùng";

    /// <summary>
    /// D1 — get-or-create (Đ-5.2, Đ-5.3). Thứ tự kiểm là một phần của hợp đồng: chính mình → 400 (trước DB) · không có hồ sơ →
    /// 404 · không phải bạn → 403 <c>not-friends</c> · rồi get-or-create. Trả <c>Created</c> để controller chọn 201/200.
    /// </summary>
    public async Task<Result<(ConversationResponse Response, bool Created)>> OpenAsync(
        Guid actorId, CreateConversationRequest request, CancellationToken ct)
    {
        if (request.UserId == actorId)
            return MessagingErrors.SelfConversation;

        var cards = await directory.GetManyAsync([request.UserId], ct);
        if (!cards.TryGetValue(request.UserId, out var peerCard))
            return MessagingErrors.UserNotFound;

        if (!await friendships.AreFriendsAsync(actorId, request.UserId, ct))
            return MessagingErrors.NotFriends;

        var (conversation, created) = await store.GetOrCreateAsync(
            ConversationPair.Of(actorId, request.UserId), Uuid7.New(), clock.GetUtcNow(), ct);

        var lastMessage = await LastMessageAsync(conversation, ct);
        return (ToResponse(conversation, actorId, ToPeer(peerCard), lastMessage, canSend: true), created);
    }

    /// <summary>D3 — chi tiết + <c>canSend</c> TÍNH SỐNG (Đ-5.3): hủy kết bạn có hiệu lực ngay ở lần đọc kế tiếp.</summary>
    public async Task<Result<ConversationResponse>> GetAsync(Guid actorId, Guid conversationId, CancellationToken ct)
    {
        var member = await access.ResolveMemberAsync(conversationId, actorId, ct);
        if (member.IsFailure)
            return member.Error!.Value;

        var conversation = member.Value!;
        var peerId = conversation.PeerOf(actorId);
        var cards = await directory.GetManyAsync([peerId], ct);
        var canSend = await friendships.AreFriendsAsync(actorId, peerId, ct);
        var lastMessage = await LastMessageAsync(conversation, ct);

        return ToResponse(conversation, actorId, PeerOrUnknown(cards, peerId), lastMessage, canSend);
    }

    /// <summary>
    /// D2 — danh sách hội thoại ĐÃ CÓ TIN của người gọi, keyset <c>(last_message_at, id) DESC</c>. Số câu SQL cố định theo
    /// trang (LIST-02): một câu danh sách, một lô người kia (<c>IUserDirectory</c>), một lô tin cuối. Không <c>canSend</c>.
    /// </summary>
    public async Task<ConversationPage> ListAsync(Guid actorId, string? rawCursor, int limit, CancellationToken ct)
    {
        ConversationCursor? cursor = ConversationCursor.TryDecode(rawCursor, out var decoded) ? decoded : null;
        var rows = await store.ListAsync(actorId, cursor, limit + 1, ct);
        var window = rows.Count > limit ? rows.Take(limit).ToList() : rows;
        if (window.Count == 0)
            return new ConversationPage([], null);

        var cards = await directory.GetManyAsync(window.Select(c => c.PeerOf(actorId)).Distinct().ToArray(), ct);
        var lastIds = window.Where(c => c.LastMessageId is not null).Select(c => c.LastMessageId!.Value).ToArray();
        var lastMessages = await store.GetMessagesAsync(lastIds, ct);

        var items = window
            .Select(c => ToResponse(
                c,
                actorId,
                PeerOrUnknown(cards, c.PeerOf(actorId)),
                c.LastMessageId is { } id && lastMessages.TryGetValue(id, out var m) ? MessageResponse.From(m) : null,
                canSend: null))
            .ToList();

        var next = rows.Count > limit ? new ConversationCursor(window[^1].LastMessageAt!.Value, window[^1].Id).Encode() : null;
        return new ConversationPage(items, next);
    }

    /// <summary>D3 — tổng chưa đọc của badge (Đ-5.14): một câu SQL, không cache (trường theo người xem).</summary>
    public async Task<UnreadCountResponse> UnreadAsync(Guid actorId, CancellationToken ct) =>
        new(await store.UnreadTotalAsync(actorId, ct));

    /// <summary>
    /// D4 — lịch sử (Mục 7.6). Không tham số / <c>cursor</c>: <c>seq DESC</c>, <c>nextCursor</c> trỏ tin cũ nhất của trang.
    /// <c>afterSeq</c>: <c>seq ASC</c>, <c>nextCursor</c> luôn <c>null</c>. Validator đã chặn cursor rác và cursor + afterSeq.
    /// </summary>
    public async Task<Result<MessagePage>> HistoryAsync(
        Guid actorId, Guid conversationId, MessageHistoryQuery query, CancellationToken ct)
    {
        var member = await access.ResolveMemberAsync(conversationId, actorId, ct);
        if (member.IsFailure)
            return member.Error!.Value;

        var limit = query.EffectiveLimit;
        if (query.AfterSeq is { } afterSeq)
        {
            var after = await store.AfterAsync(conversationId, afterSeq, limit, ct);
            return new MessagePage(after.Select(MessageResponse.From).ToList(), null);
        }

        long? beforeSeq = MessageCursor.TryDecode(query.Cursor, out var cursor) ? cursor.Seq : null;
        var rows = await store.HistoryAsync(conversationId, beforeSeq, limit + 1, ct);
        var window = rows.Count > limit ? rows.Take(limit).ToList() : rows;
        var next = rows.Count > limit ? new MessageCursor(window[^1].Seq).Encode() : null;
        return new MessagePage(window.Select(MessageResponse.From).ToList(), next);
    }

    /// <summary>
    /// D6 — biên nhận (Đ-5.6). Chỉ thành viên; KHÔNG cần đang là bạn (hủy kết bạn vẫn đánh dấu đã xem được — Đ-5.3). Mốc thật
    /// sự tăng thì đẩy <c>ReceiptUpdated</c> tới cả hai (sau COMMIT của câu UPDATE); không đổi gì thì im lặng.
    /// </summary>
    public async Task<Result> ReceiptAsync(
        Guid actorId, Guid conversationId, ReceiptKind kind, long upToSeq, CancellationToken ct)
    {
        var member = await access.ResolveMemberAsync(conversationId, actorId, ct);
        if (member.IsFailure)
            return member.Error!.Value;

        var conversation = member.Value!;
        var marks = await store.AdvanceMarksAsync(
            conversationId, actorId == conversation.UserAId, kind, upToSeq, clock.GetUtcNow(), ct);

        if (marks is { } m)
            await notifier.ReceiptUpdatedAsync(
                new ReceiptUpdatedEvent(conversationId, actorId, m.DeliveredSeq, m.SeenSeq),
                conversation.UserAId, conversation.UserBId, ct);

        return Result.Success();
    }

    private async Task<MessageResponse?> LastMessageAsync(Conversation conversation, CancellationToken ct)
    {
        if (conversation.LastMessageId is not { } id)
            return null;

        var messages = await store.GetMessagesAsync([id], ct);
        return messages.TryGetValue(id, out var m) ? MessageResponse.From(m) : null;
    }

    private ConversationPeer PeerOrUnknown(IReadOnlyDictionary<Guid, UserCard> cards, Guid peerId) =>
        cards.TryGetValue(peerId, out var card) ? ToPeer(card) : new ConversationPeer(peerId, UnknownPeerName, null);

    private ConversationPeer ToPeer(UserCard card) =>
        new(card.UserId, card.DisplayName, card.AvatarKey is { } key ? storage.CreatePresignedGet(key) : null);

    private static ConversationResponse ToResponse(
        Conversation c, Guid actorId, ConversationPeer peer, MessageResponse? lastMessage, bool? canSend)
    {
        var mine = c.MarksOf(actorId);
        var theirs = c.MarksOf(c.PeerOf(actorId));
        return new ConversationResponse(
            c.Id, peer, lastMessage, mine.UnreadCount(c.SeqCounter), theirs.DeliveredSeq, theirs.SeenSeq, canSend);
    }
}
