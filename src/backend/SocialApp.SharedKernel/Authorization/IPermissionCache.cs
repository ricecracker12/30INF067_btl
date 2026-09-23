namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Vai trò → tập mã quyền, có cache. PermissionHandler (C2) đọc qua đây, không gọi thẳng nguồn.
///
/// Kiểm "vai trò X có quyền Y không" thì dùng <see cref="PermissionChecks.IsAllowedAsync"/> — hàm DUY NHẤT gói Admin
/// short-circuit + tra cache (giai-doan-6.md Mục 6.2), không tự gọi <see cref="GetAsync"/> rồi tự so "ADMIN".
/// </summary>
public interface IPermissionCache
{
    ValueTask<IReadOnlySet<string>> GetAsync(string roleCode, CancellationToken ct = default);

    /// <summary>
    /// Xóa entry của một vai trò NGAY — request kế tiếp đọc lại nguồn (Đ-6.10, GĐ6). Gọi sau <c>COMMIT</c> của thao tác sửa
    /// <c>role_permissions</c> hoặc xóa vai trò; đừng gọi thẳng — dùng <see cref="IPermissionChangeNotifier"/> để instance khác
    /// cũng biết.
    /// </summary>
    void Invalidate(string roleCode);

    /// <summary>Xóa mọi entry — khi không biết đã lỡ mất thay đổi nào (Redis vừa nối lại, tin pub/sub lúc rớt đã mất).</summary>
    void InvalidateAll();
}
