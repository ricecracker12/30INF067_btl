namespace SocialApp.Modules.Identity.Presentation;

/// <summary>
/// Nhóm Swagger THỨ HAI của module Identity (GĐ6 Đ-6.1): màn quản trị tài khoản và vai trò — <c>/admin/users*</c>,
/// <c>/admin/roles*</c>, <c>/admin/permissions</c>. Ở Identity chứ không ở Moderation vì mọi thứ các endpoint này ghi
/// (<c>users.status</c>, <c>users.role_id</c>, <c>roles</c>, <c>role_permissions</c>, <c>refresh_tokens</c>) là bảng của Identity.
///
/// Tách khỏi <see cref="IdentityApiGroup"/> vì là bề mặt khác người dùng: <c>identity-v1</c> là của mọi người dùng, <c>admin-v1</c>
/// chỉ cho người có quyền quản trị, mang <c>email</c> của người khác (PII, Mục 8.2) và fail-closed khi Redis chết (Đ-6.8). FE sinh
/// type riêng ở <c>lib/api/admin/</c>.
///
/// <see cref="Name"/> xuất hiện ở ba nơi và phải khớp cả ba: <c>[ApiExplorerSettings]</c> trên controller, dòng <c>apiGroups</c>
/// ở Program.cs, và tên file hợp đồng <c>admin-v1.yaml</c> cùng thư mục. Lệch một chỗ thì endpoint biến khỏi Swagger mà không
/// lỗi nào — <c>AdminContractTests</c> đỏ đúng chỗ. <c>PrivilegedEndpointTests</c> đọc CHUỖI <c>"admin-v1"</c> (không tham chiếu
/// module), nên đổi tên nhóm là phải sửa cả ở đó.
/// </summary>
public static class AdminApiGroup
{
    /// <summary>Tên nhóm, trùng tên file hợp đồng <c>admin-v1.yaml</c>.</summary>
    public const string Name = "admin-v1";

    /// <summary>Nhãn hiển thị trên dropdown của Swagger UI.</summary>
    public const string Title = "Quản trị";
}
