namespace SocialApp.Modules.Identity.Domain;

/// <summary>
/// Quyền đơn lẻ (ENT-10a, bảng <c>permissions</c>). Mười tám dòng (17 của Mục 5.2 + <c>role.manage</c> của GĐ6), <c>permission_id</c>
/// gán tay theo vị trí trong <see cref="PermissionCodes.All"/> nên cũng KHÔNG để EF tự sinh.
/// </summary>
public sealed class Permission
{
    /// <summary>Khóa chính <c>smallint</c>, gán tay 1..18. A2 phải cấu hình <c>ValueGeneratedNever()</c>.</summary>
    public required short PermissionId { get; init; }

    /// <summary>Mã dạng <c>resource.action</c>, một trong <see cref="PermissionCodes"/>. <c>varchar(40)</c>, unique.</summary>
    public required string Code { get; init; }

    /// <summary>Mô tả tùy chọn, <c>varchar(120)</c>.</summary>
    public string? Description { get; set; }
}
