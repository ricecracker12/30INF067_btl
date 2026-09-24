namespace SocialApp.SharedKernel.Realtime;

/// <summary>
/// Hằng của vé realtime (Đ-E16, giai-doan-5.md Đ-5.9). Ở SharedKernel vì MỌI hub dùng chung một vé: <c>/hubs/chat</c> (GĐ5) và
/// <c>/hubs/notifications</c> (GĐ6, Đ-6.18) — để trong Messaging thì Notification phải import Messaging (ArchUnitNET chặn).
/// </summary>
public static class RealtimeTicketDefaults
{
    /// <summary>Tên scheme xác thực của hub. Hub khai <c>[Authorize(AuthenticationSchemes = Scheme)]</c>; REST KHÔNG BAO GIỜ dùng.</summary>
    public const string Scheme = "RealtimeTicket";

    /// <summary>Tham số query mang vé — tên mà SignalR JS client dùng cho <c>accessTokenFactory</c> khi đi WebSocket.</summary>
    public const string QueryKey = "access_token";

    /// <summary>Scheme vé CHỈ đọc query ở đường này; ngoài nó thì không có kết quả (REST không nhận vé — HUB-06).</summary>
    public const string HubsPathPrefix = "/hubs";

    /// <summary>Vé sống 30 giây (Đ-E16): đủ cho một lần bắt tay, quá ngắn để lộ ra có giá trị.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    /// <summary>Policy rate limit của <c>POST /realtime/tickets</c>: 20/phút/user (Đ-5.9) — mạng chập chờn làm client tự nối lại liên tục.</summary>
    public const string RateLimitPolicy = "realtime-ticket";

    /// <summary>Hạn mức của <see cref="RateLimitPolicy"/>.</summary>
    public const int RateLimitPerMinute = 20;
}
