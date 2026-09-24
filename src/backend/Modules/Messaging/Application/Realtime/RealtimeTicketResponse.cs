namespace SocialApp.Modules.Messaging.Application.Realtime;

/// <summary>
/// Kết quả của <c>POST /api/v1/realtime/tickets</c> (Mục 8.1 <c>RealtimeTicket</c>). Vé dùng cho MỌI <c>/hubs/*</c> — hub chat của
/// GĐ5 và hub thông báo của GĐ6 (Đ-6.18). <see cref="ExpiresIn"/> tính bằng giây.
/// </summary>
public sealed record RealtimeTicketResponse(string Ticket, int ExpiresIn);
