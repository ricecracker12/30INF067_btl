namespace SocialApp.Modules.Notification.Presentation;

/// <summary>
/// Nhóm Swagger của module Notification (GĐ6 Đ-6.1, Mục 8.3): danh sách, số chưa đọc, đánh dấu đã đọc. FE sinh type ở
/// <c>lib/api/notification/</c>. Hub thông báo (C6) không ở nhóm này — hợp đồng hub là file <c>.md</c> riêng.
///
/// Không endpoint nào của nhóm này là đặc quyền: không <c>[PrivilegedEndpoint]</c>, không fail-closed, không 503.
///
/// <see cref="Name"/> xuất hiện ở ba nơi và phải khớp cả ba: <c>[ApiExplorerSettings]</c> trên controller, dòng <c>apiGroups</c>
/// ở Program.cs, và tên file hợp đồng <c>notification-v1.yaml</c> cùng thư mục. Lệch một chỗ thì endpoint biến khỏi Swagger mà
/// không lỗi nào — <c>NotificationContractTests</c> đỏ đúng chỗ.
/// </summary>
public static class NotificationApiGroup
{
    /// <summary>Tên nhóm, trùng tên file hợp đồng <c>notification-v1.yaml</c>.</summary>
    public const string Name = "notification-v1";

    /// <summary>Nhãn hiển thị trên dropdown của Swagger UI.</summary>
    public const string Title = "Thông báo";
}
