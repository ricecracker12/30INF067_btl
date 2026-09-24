namespace SocialApp.Modules.Messaging.Application.Conversations;

/// <summary>
/// Đẩy sự kiện realtime cho hai thành viên (Đ-5.8). Application định nghĩa, Presentation hiện thực bằng <c>IHubContext</c> —
/// service không biết SignalR, và REST lẫn hub dùng CHUNG một đường đẩy. Người nhận do SERVER chọn từ dữ liệu hội thoại, không
/// bao giờ do client gửi lên; không có group, không có <c>Clients.All</c> (B.10 điều 6).
///
/// Gọi SAU <c>COMMIT</c>: đẩy trước commit là người nhận có thể thấy một tin "ma" bị rollback (Đ-5.4 bước 6).
/// </summary>
public interface IChatNotifier
{
    Task MessageReceivedAsync(MessageResponse message, Guid userA, Guid userB, CancellationToken ct);

    Task ReceiptUpdatedAsync(ReceiptUpdatedEvent receipt, Guid userA, Guid userB, CancellationToken ct);
}
