using SocialApp.Modules.Identity.Application.Me;
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
    /// Tra user theo email kèm roles.code. Không phân biệt hoa thường (cột citext) — hiện thực phải so bằng tham số mang
    /// kiểu của cột: SQL thô với tham số text thì phân biệt hoa thường mà không báo lỗi.
    /// </summary>
    Task<LoginCandidate?> FindForLoginAsync(string email, CancellationToken ct);

    /// <summary>
    /// Tăng bộ đếm sai bằng MỘT câu UPDATE nguyên tử; đủ <see cref="Domain.LockoutPolicy.MaxFailedAttempts"/> thì khóa
    /// <see cref="Domain.LockoutPolicy.LockDuration"/> và reset bộ đếm (Mục 7.2 bước 4). Không đọc-rồi-ghi: dò mật khẩu
    /// song song sẽ đếm sót.
    /// </summary>
    Task RegisterFailedLoginAsync(Guid userId, DateTimeOffset now, CancellationToken ct);

    /// <summary>Đăng nhập thành công: bộ đếm về 0 và bỏ <c>locked_until</c> (Mục 7.2 bước 6).</summary>
    Task ResetFailedLoginAsync(Guid userId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Hồ sơ rút gọn cho <c>GET /me</c>, join <c>roles</c> lấy CẢ <c>code</c> lẫn <c>display_name</c> (quyết định 3).
    /// <c>null</c> nếu user không còn trong DB.
    /// </summary>
    Task<MeResponse?> FindMeAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Trong MỘT transaction: INSERT user + token → gọi <paramref name="beforeCommit"/> (gửi mail) → COMMIT (Đ-D5).
    /// Email đã tồn tại → <c>false</c> và <paramref name="beforeCommit"/> KHÔNG được gọi. Phát hiện bằng unique
    /// violation, không bằng SELECT trước: đọc-rồi-ghi thì hai request song song cùng qua bước kiểm và cái sau 500.
    /// <paramref name="beforeCommit"/> ném → rollback, exception thoát ra.
    /// </summary>
    Task<bool> AddWithVerificationAsync(User user, EmailVerificationToken token, Func<Task> beforeCommit, CancellationToken ct);
}
