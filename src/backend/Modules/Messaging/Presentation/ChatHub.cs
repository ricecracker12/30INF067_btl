using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SocialApp.SharedKernel.Realtime;

namespace SocialApp.Modules.Messaging.Presentation;

/// <summary>
/// Hub nhắn tin 1-1 ở <c>/hubs/chat</c> — hợp đồng <c>chat-hub-v1.md</c>. Xác thực CHỈ bằng vé realtime (scheme
/// <see cref="RealtimeTicketDefaults.Scheme"/>): bearer không bao giờ được nhận ở đây (HUB-05).
///
/// Filter thu hồi + tuổi thọ 15 phút là filter TOÀN CỤC ở SharedKernel (Đ-5.10), không khai ở đây. <c>actorId</c> luôn là
/// <c>Context.UserIdentifier</c> (claim <c>sub</c> của vé) — không bao giờ từ tham số của phương thức (Đ-5.7).
/// </summary>
[Authorize(AuthenticationSchemes = RealtimeTicketDefaults.Scheme)]
public sealed class ChatHub : Hub
{
    /// <summary>Đường map ở host. Hằng ở đây để test và Program.cs không gõ tay chuỗi.</summary>
    public const string Path = "/hubs/chat";
}
