using Microsoft.AspNetCore.SignalR;
using SocialApp.Modules.Messaging.Application.Conversations;

namespace SocialApp.Modules.Messaging.Presentation;

/// <summary>
/// Hiện thực <see cref="IChatNotifier"/> bằng <see cref="IHubContext{THub}"/> (Đ-5.8): đẩy tới <c>Clients.Users(a, b)</c> — mọi
/// kết nối (mọi tab, mọi thiết bị) của CẢ HAI thành viên. Người nhận luôn là hai id lấy từ DÒNG hội thoại trong DB, không bao
/// giờ từ client; không group, không <c>Clients.All</c> (HUB-09 canh). Không ai đang kết nối thì SignalR bỏ qua, không lỗi — tin
/// đã lưu bền (Mục 7.3).
/// </summary>
public sealed class ChatHubNotifier(IHubContext<ChatHub> hub) : IChatNotifier
{
    public const string MessageReceived = "MessageReceived";
    public const string ReceiptUpdated = "ReceiptUpdated";

    public Task MessageReceivedAsync(MessageResponse message, Guid userA, Guid userB, CancellationToken ct) =>
        hub.Clients.Users(userA.ToString(), userB.ToString()).SendAsync(MessageReceived, message, ct);

    public Task ReceiptUpdatedAsync(ReceiptUpdatedEvent receipt, Guid userA, Guid userB, CancellationToken ct) =>
        hub.Clients.Users(userA.ToString(), userB.ToString()).SendAsync(ReceiptUpdated, receipt, ct);
}
