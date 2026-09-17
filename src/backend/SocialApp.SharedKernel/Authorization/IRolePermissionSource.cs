namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Nguồn thật của ma trận quyền. Identity hiện thực ở C5 (join roles → role_permissions → permissions).
/// Nhận role CODE dạng chuỗi; phép dịch code → role_id nằm gọn trong hiện thực (Mục 3.1).
///
/// Không có hiện thực giả nào trong src/backend/ hay integration test (Đ3) — nguồn giả chỉ sống trong unit test.
/// </summary>
public interface IRolePermissionSource
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(string roleCode, CancellationToken ct = default);
}
