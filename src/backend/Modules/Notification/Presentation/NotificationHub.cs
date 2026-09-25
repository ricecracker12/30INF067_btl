using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SocialApp.Modules.Notification.Application;
using SocialApp.SharedKernel.Realtime;

namespace SocialApp.Modules.Notification.Presentation;

/// <summary>
/// Hub thông báo ở <c>/hubs/notifications</c> (C6, Đ-6.18) — hợp đồng <c>notification-hub-v1.md</c>. Dùng LẠI mọi thứ của GĐ5, không viết
/// lại gì: xác thực CHỈ bằng vé realtime (scheme <see cref="RealtimeTicketDefaults.Scheme"/>, <c>?access_token=</c>, dùng một lần), filter
/// thu hồi + tuổi thọ 15 phút và presence là filter TOÀN CỤC ở SharedKernel, <c>IUserIdProvider</c> đọc <c>sub</c> của vé.
///
/// <b>KHÔNG có phương thức nào client gọi được</b> — chỉ server → client (<see cref="NotificationHubPusher.NotificationUpserted"/>). Không
/// phương thức nào = không cửa tầng 3 nào phải canh trên hub này. Thêm một phương thức public ở đây là đổi hợp đồng
/// (<c>NotificationHubContractTests</c> đỏ).
///
/// Hệ quả của presence toàn cục (Đ-5.11): tab chỉ nối hub thông báo cũng tính là "online" — đúng nghĩa "đang mở app" mà
/// <c>MessageSentHandler</c> dùng để quyết định có tạo thông báo tin nhắn không.
/// </summary>
[Authorize(AuthenticationSchemes = RealtimeTicketDefaults.Scheme)]
public sealed class NotificationHub : Hub
{
    /// <summary>Đường map ở host. Hằng ở đây để test và Program.cs không gõ tay chuỗi.</summary>
    public const string Path = "/hubs/notifications";

    /// <summary>Tên sự kiện server → client — tập này phải BẰNG tập <c>events</c> của <c>notification-hub-v1.examples.json</c>.</summary>
    public static readonly string[] Events = [NotificationHubPusher.NotificationUpserted];
}

/// <summary>
/// Hiện thực <see cref="INotificationPusher"/> bằng <see cref="IHubContext{THub}"/>: <c>Clients.User(recipient)</c> — mọi kết nối (mọi tab,
/// mọi thiết bị) của ĐÚNG người nhận dòng thông báo. Không ai đang nối thì SignalR bỏ qua, không lỗi — thông báo đã lưu bền.
/// </summary>
public sealed class NotificationHubPusher(IHubContext<NotificationHub> hub) : INotificationPusher
{
    public const string NotificationUpserted = "NotificationUpserted";

    public Task PushAsync(Guid recipientId, NotificationUpsertedEvent notification, CancellationToken ct) =>
        hub.Clients.User(recipientId.ToString()).SendAsync(NotificationUpserted, notification, ct);
}
