using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SocialApp.Modules.Messaging.Domain;
using SocialApp.SharedKernel.Authorization;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Ids;
using SocialApp.SharedKernel.Observability;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Messaging.Application.Conversations;

/// <summary>Kênh vào của một lượt gửi — chỉ để đếm metric (<c>socialapp_messages_sent_total{channel}</c>).</summary>
public enum SendChannel
{
    Hub,
    Rest,
}

/// <summary>Kết quả thành công của một lượt gửi (Đ-5.5): <see cref="Replayed"/> = tin đã có từ trước với cùng nội dung.</summary>
public sealed record SendResult(MessageResponse Message, bool Replayed);

/// <summary>
/// Đ-5.7 — MỘT service gửi tin, HAI cửa vào (hub <c>SendMessage</c> và <c>POST …/messages</c>); chỗ DUY NHẤT chứa Đ-5.4/Đ-5.5.
/// Cả hai cửa đi đủ ba tầng ở đây:
///
/// 1. Tầng 2 <c>message.send</c> qua <see cref="IPermissionCache"/> (REST có thêm <c>[RequirePermission]</c>; hub không có
///    attribute tương đương cho từng phương thức nên kiểm ở đây — cùng cách Đ-2.6 kiểm <c>post.create</c>). Admin short-circuit
///    CHỈ ở tầng 2 (<c>PermissionChecks</c>).
/// 2. Tầng 3 BR-06 qua <see cref="ConversationAccess"/> — không có nhánh Admin: Admin không gửi được vào hội thoại người khác.
/// 3. BR-09 <c>AreFriendsAsync</c> — đọc thẳng DB, KHÔNG nằm trong transaction (Đ-5.4: giữ khóa dòng trong lúc chờ truy vấn
///    của module khác là kéo dài hàng đợi của cả hội thoại).
///
/// Sau <c>COMMIT</c>: metric → event <c>MessageSent</c> → đẩy <c>MessageReceived</c>. Đẩy hỏng KHÔNG làm hỏng ACK (tin đã lưu bền;
/// người nhận sẽ nạp qua REST). Không log nội dung tin ở bất kỳ mức nào (Đ-5.18) — chỉ id, seq, độ dài.
/// </summary>
public sealed class MessageSendService(
    IConversationStore store,
    ConversationAccess access,
    IFriendshipReader friendships,
    IPermissionCache permissions,
    IChatNotifier notifier,
    MessagingEvents events,
    TimeProvider clock,
    ILogger<MessageSendService> logger)
{
    public async Task<Result<SendResult>> SendAsync(
        Guid actorId, string? role, Guid conversationId, string? content, Guid clientMsgId, SendChannel channel,
        CancellationToken ct)
    {
        if (!await permissions.IsAllowedAsync(role, MessagingPermissions.MessageSend, ct))
            return Result<SendResult>.Forbidden();

        if (MessageContentPolicy.Validate(content) is { } invalid)
            return Error.Validation(MessageContentPolicy.ContentKey, invalid);
        if (clientMsgId == Guid.Empty)
            return Error.Validation("clientMsgId", SendMessageRequestValidator.ClientMsgIdRequired);

        var member = await access.ResolveMemberAsync(conversationId, actorId, ct);
        if (member.IsFailure)
            return member.Error!.Value;

        var conversation = member.Value!;
        var recipientId = conversation.PeerOf(actorId);
        if (!await friendships.AreFriendsAsync(actorId, recipientId, ct))
            return MessagingErrors.NotFriends;

        var outcome = await store.SendAsync(
            new SendCommand(
                conversationId, actorId == conversation.UserAId, actorId, Uuid7.New(), content!, clientMsgId, clock.GetUtcNow()),
            ct);

        switch (outcome.Status)
        {
            case SendStatus.Replayed:
                return new SendResult(MessageResponse.From(outcome.Message!), Replayed: true);
            case SendStatus.ClientMsgIdReused:
                return MessagingErrors.ClientMsgIdReused;
            case SendStatus.NotFound:
                return Result<SendResult>.Forbidden();
        }

        // --- SAU COMMIT ---
        var message = MessageResponse.From(outcome.Message!);
        BusinessMetrics.MessageSent(channel == SendChannel.Hub ? "hub" : "rest");
        events.MessageSent(conversationId, message.MessageId, actorId, recipientId, message.Seq);
        await PushAsync(message, conversation, ct);

        return new SendResult(message, Replayed: false);
    }

    private async Task PushAsync(MessageResponse message, Conversation conversation, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await notifier.MessageReceivedAsync(message, conversation.UserAId, conversation.UserBId, ct);
            BusinessMetrics.MessagePushed(Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Tin ĐÃ lưu bền — người nhận nạp lại qua REST khi mở app / nối lại (Mục 7.3). Không log nội dung (Đ-5.18).
            logger.LogWarning(ex,
                "Không đẩy được MessageReceived cho tin {MessageId} (hội thoại {ConversationId}, seq {Seq}, {Length} ký tự)",
                message.MessageId, message.ConversationId, message.Seq, message.Content.Length);
        }
    }
}
