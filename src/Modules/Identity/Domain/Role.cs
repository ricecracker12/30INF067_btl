namespace SocialApp.Modules.Identity.Domain;

/// <summary>
/// Vai trò (ENT-10, bảng <c>roles</c>). Tách <see cref="Code"/> khỏi <see cref="DisplayName"/> theo
/// quyết định 3.3: <c>Code</c> là bất biến để JWT, policy, seeder và test bám vào; <c>DisplayName</c>
/// là chuỗi hiển thị, đổi thoải mái, kể cả đổi ngôn ngữ, mà không phá thứ gì.
///
/// <see cref="RoleId"/> do seeder gán tay (1/2/3 — Mục 5.1), KHÔNG để EF tự sinh: <c>role_permissions</c>
/// trỏ vào các id cố định đó.
/// </summary>
public sealed class Role
{
    /// <summary>Khóa chính <c>smallint</c>, gán tay theo Mục 5.1. A2 phải cấu hình <c>ValueGeneratedNever()</c>.</summary>
    public required short RoleId { get; init; }

    /// <summary>Mã bất biến, một trong <see cref="RoleCodes"/>. <c>varchar(30)</c>, unique.</summary>
    public required string Code { get; init; }

    /// <summary>Tên hiển thị, <c>varchar(50)</c>. Đổi được bất cứ lúc nào — không ai bám vào nó.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Mô tả tùy chọn, <c>varchar(120)</c>.</summary>
    public string? Description { get; set; }

    /// <summary>Thời điểm tạo. DB cũng có <c>DEFAULT now()</c> cho đường seed bằng SQL thô.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Thời điểm sửa gần nhất.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
