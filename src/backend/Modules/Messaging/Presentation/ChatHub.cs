using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SocialApp.Modules.Messaging.Application.Conversations;
using SocialApp.SharedKernel.Authentication;
using SocialApp.SharedKernel.Realtime;
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Messaging.Presentation;

/// <summary>
/// Hub nhắn tin 1-1 ở <c>/hubs/chat</c> — hợp đồng <c>chat-hub-v1.md</c>. Xác thực CHỈ bằng vé realtime (scheme
/// <see cref="RealtimeTicketDefaults.Scheme"/>): bearer không bao giờ được nhận ở đây (HUB-05).
///
/// Vỏ mỏng (Đ-5.7): gọi đúng service mà REST gọi, dịch <see cref="Result"/> sang MÃ lỗi hub (Mục 8.2). Filter thu hồi + tuổi thọ
/// 15 phút là filter TOÀN CỤC ở SharedKernel (Đ-5.10). <c>actorId</c> luôn là <c>Context.UserIdentifier</c> (claim <c>sub</c> của
/// vé) — tham số hub không có trường nào mang id người gửi (B.10 điều 1).
///
/// Không có group, không <c>Clients.All</c>: người nhận do <see cref="ChatHubNotifier"/> chọn từ dữ liệu hội thoại (Đ-5.8).
/// </summary>
[Authorize(AuthenticationSchemes = RealtimeTicketDefaults.Scheme)]
public sealed class ChatHub(
    MessageSendService sender,
    ConversationService conversations,
    HubSendRateLimiter limiter,
    ILogger<ChatHub> logger) : Hub
{
    /// <summary>Đường map ở host. Hằng ở đây để test và Program.cs không gõ tay chuỗi.</summary>
    public const string Path = "/hubs/chat";

    /// <summary>Tên sự kiện server → client — tập này phải BẰNG tập <c>events</c> của <c>chat-hub-v1.examples.json</c> (B4).</summary>
    public static readonly string[] Events = [ChatHubNotifier.MessageReceived, ChatHubNotifier.ReceiptUpdated];

    /// <summary>Gửi tin — ACK "Đã gửi" (chat-hub-v1.md Mục 2). Lỗi là <see cref="HubException"/> mang MÃ, không mang câu.</summary>
    public async Task<SendMessageResult> SendMessage(SendMessageArgs args)
    {
        var actorId = ActorId();
        if (!limiter.TryAcquire(actorId))
            throw new HubException(HubErrorCodes.RateLimited);

        var result = await GuardAsync(() => sender.SendAsync(
            actorId, Context.User?.FindFirstValue(JwtClaims.Role), args.ConversationId, args.Content, args.ClientMsgId,
            SendChannel.Hub, Context.ConnectionAborted));
        if (result.IsFailure)
            throw new HubException(HubErrorCodes.From(result.Error!.Value));

        return new SendMessageResult(result.Value!.Message, result.Value.Replayed);
    }

    /// <summary>Biên nhận đã nhận/đã xem (Đ-5.6). Chỉ thành viên; không cần đang là bạn.</summary>
    public async Task SendReceipt(SendReceiptArgs args)
    {
        if (args.UpToSeq < 1)
            throw new HubException(HubErrorCodes.Validation);

        var result = await GuardAsync(() =>
            conversations.ReceiptAsync(ActorId(), args.ConversationId, args.Kind, args.UpToSeq, Context.ConnectionAborted));
        if (result.IsFailure)
            throw new HubException(HubErrorCodes.From(result.Error!.Value));
    }

    private Guid ActorId() => Guid.Parse(Context.UserIdentifier!);

    /// <summary>
    /// Lỗi hạ tầng (DB chết…) → mã <c>unavailable</c>: client KHÔNG biết tin đã lưu chưa → gửi lại cùng <c>clientMsgId</c>
    /// (Mục 8.2 quy tắc 5). Không để SignalR trả câu "unexpected error" chung chung — client không phân nhánh được theo nó.
    /// </summary>
    private async Task<T> GuardAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not (HubException or OperationCanceledException))
        {
            logger.LogError(ex, "Lời gọi hub thất bại (kết nối {ConnectionId})", Context.ConnectionId);
            throw new HubException(HubErrorCodes.Unavailable);
        }
    }
}
