namespace SocialApp.Modules.Moderation.Presentation;

/// <summary>
/// Nhóm Swagger của module Moderation (GĐ6 Đ-6.1, Mục 8.1): báo cáo, hàng đợi kiểm duyệt, quyết định, khôi phục, nhật ký kiểm toán.
/// FE sinh type ở <c>lib/api/moderation/</c>.
///
/// Mọi action của nhóm này mang <c>[PrivilegedEndpoint]</c> — TRỪ đúng <c>POST /reports</c> (B.10 #8): người dùng thường gửi báo
/// cáo, fail-closed ở đó là chặn người dùng vì Redis. <c>PrivilegedEndpointTests</c> đọc CHUỖI <c>"moderation-v1"</c> (không tham
/// chiếu module), nên đổi tên nhóm là phải sửa cả ở đó.
///
/// <see cref="Name"/> xuất hiện ở ba nơi và phải khớp cả ba: <c>[ApiExplorerSettings]</c> trên controller, dòng <c>apiGroups</c>
/// ở Program.cs, và tên file hợp đồng <c>moderation-v1.yaml</c> cùng thư mục. Lệch một chỗ thì endpoint biến khỏi Swagger mà
/// không lỗi nào — <c>ModerationContractTests</c> đỏ đúng chỗ.
/// </summary>
public static class ModerationApiGroup
{
    /// <summary>Tên nhóm, trùng tên file hợp đồng <c>moderation-v1.yaml</c>.</summary>
    public const string Name = "moderation-v1";

    /// <summary>Nhãn hiển thị trên dropdown của Swagger UI.</summary>
    public const string Title = "Kiểm duyệt";
}
