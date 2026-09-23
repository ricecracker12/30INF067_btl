namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Báo "tập quyền của vai trò X vừa đổi" (Đ-6.10, GĐ6): xóa cache quyền TẠI CHỖ rồi phát cho mọi instance khác qua Redis pub/sub.
///
/// Một hàm cho cả hai việc (L-C4 của hướng dẫn khối A+C) — hai lời gọi riêng thì một ngày có người gọi thiếu một: quên xóa tại
/// chỗ thì chính instance xử lý request vẫn cũ 60 giây; quên phát thì instance KHÁC cũ 60 giây và không test một-instance nào bắt.
/// </summary>
public interface IPermissionChangeNotifier
{
    /// <summary>
    /// Gọi SAU <c>COMMIT</c> của mọi thao tác sửa <c>role_permissions</c> hoặc xóa vai trò (D5). Không ném khi Redis chết: thay đổi
    /// đã lưu, instance khác thấy sau ≤ 60 giây (TTL là lưới cuối) — đây là thêm/bớt quyền của cả một vai trò, không khẩn như hạ
    /// quyền một người (thứ đó đi qua <c>revoked:user</c>, Đ-6.6).
    /// </summary>
    Task NotifyAsync(string roleCode, CancellationToken ct = default);
}
