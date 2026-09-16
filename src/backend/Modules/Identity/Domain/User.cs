using SocialApp.SharedKernel.Ids;

namespace SocialApp.Modules.Identity.Domain;

/// <summary>
/// Tài khoản người dùng (ENT-01, bảng <c>users</c>).
///
/// <see cref="RoleId"/> là <c>short</c> chứ không phải chuỗi: DB giữ quan hệ bằng khóa ngoại
/// <c>ON DELETE RESTRICT</c> — đó là biện pháp #1 thay cho cột <c>is_system</c> đã bị loại bỏ
/// (Mục 3.4), làm cho việc xóa một vai trò đang có người dùng là KHÔNG THỂ ở tầng dưới cùng.
/// Chuỗi <c>code</c> chỉ xuất hiện khi phát JWT, đọc qua bảng <c>roles</c>.
///
/// KHÔNG có navigation property <c>Role</c>: GĐ1 chưa cần, và mỗi navigation là một đường để code
/// sau này vô tình <c>.Include()</c> cả bảng. Thêm khi có nơi thực sự dùng.
/// </summary>
public sealed class User
{
    /// <summary>Khóa chính UUID v7 (Mục 4) — tuần tự theo thời gian, không phân mảnh index.</summary>
    public Guid UserId { get; init; } = Uuid7.New();

    /// <summary>Email đăng nhập. Cột là <c>citext</c> nên so sánh không phân biệt hoa thường, unique.</summary>
    public required string Email { get; set; }

    /// <summary>
    /// Hash BCrypt cost 12 (chuỗi 60 ký tự, cột <c>varchar(72)</c> để dư chỗ). Mật khẩu bản rõ
    /// KHÔNG bao giờ rời khỏi request handler.
    /// </summary>
    public required string PasswordHash { get; set; }

    /// <summary>FK → <c>roles.role_id</c>, ON DELETE RESTRICT.</summary>
    public required short RoleId { get; set; }

    /// <summary>Thời điểm xác minh email; <c>null</c> nghĩa là chưa xác minh.</summary>
    public DateTimeOffset? EmailVerifiedAt { get; set; }

    /// <summary>Số lần đăng nhập sai liên tiếp — đầu vào của lockout (FR-003). Kiểu <c>short</c>, khớp <c>smallint</c>.</summary>
    public short FailedLoginCount { get; set; }

    /// <summary>Khóa tới thời điểm này; <c>null</c> nghĩa là không bị khóa.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>Một trong <see cref="UserStatus"/>. DB có CHECK <c>ck_users_status</c> canh lại.</summary>
    public string Status { get; set; } = UserStatus.Active;

    /// <summary>Thời điểm tạo.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Thời điểm sửa gần nhất.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
