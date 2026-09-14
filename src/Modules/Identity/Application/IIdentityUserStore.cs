using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Application;

/// <summary>
/// Bảng <c>users</c> cho các luồng auth. Hiện thực EF ở Infrastructure/Persistence — Application không chạm EF (Đ-D1).
/// </summary>
public interface IIdentityUserStore
{
    /// <summary>roles.code → role_id (Mục 3.1). Thiếu vai trò thì ném: dữ liệu nền hỏng, không phải lỗi người dùng.</summary>
    Task<short> GetRoleIdAsync(string roleCode, CancellationToken ct);

    /// <summary>
    /// Trong MỘT transaction: INSERT user + token → gọi <paramref name="beforeCommit"/> (gửi mail) → COMMIT (Đ-D5).
    /// Email đã tồn tại → <c>false</c> và <paramref name="beforeCommit"/> KHÔNG được gọi. Phát hiện bằng unique
    /// violation, không bằng SELECT trước: đọc-rồi-ghi thì hai request song song cùng qua bước kiểm và cái sau 500.
    /// <paramref name="beforeCommit"/> ném → rollback, exception thoát ra.
    /// </summary>
    Task<bool> AddWithVerificationAsync(User user, EmailVerificationToken token, Func<Task> beforeCommit, CancellationToken ct);
}
