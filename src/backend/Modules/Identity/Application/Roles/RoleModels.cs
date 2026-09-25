using System.ComponentModel.DataAnnotations;
using FluentValidation;
using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Application.Roles;

/// <summary>
/// Một vai trò trên màn quản trị — schema <c>RoleSummary</c> của <c>admin-v1.yaml</c> (Mục 8.2).
/// <list type="bullet">
/// <item><paramref name="IsSystem"/> TÍNH từ mã (<c>USER</c>, <c>MODERATOR</c>, <c>ADMIN</c>), không phải cột — đúng quyết định bỏ
/// <c>is_system</c> của GĐ1 Mục 3.4.</item>
/// <item><paramref name="Editable"/> = sửa được tập quyền: mọi vai trò trừ ADMIN (ADMIN có mọi quyền qua short-circuit, Đ-6.9).</item>
/// <item><paramref name="Permissions"/> là quyền HIỆU LỰC (<see cref="EffectivePermissions"/>, dùng chung với <c>/me</c>): ADMIN = cả
/// 18 mã; vai trò khác = đúng tập <c>role_permissions</c>, theo thứ tự <c>permission_id</c>.</item>
/// <item><paramref name="UserCount"/> đếm MỌI tài khoản mang vai trò, không lọc <c>status</c> — tài khoản bị khóa mở lại vẫn mang quyền.</item>
/// </list>
/// </summary>
public sealed record RoleSummary(
    short RoleId,
    string Code,
    string DisplayName,
    bool IsSystem,
    bool Editable,
    int UserCount,
    IReadOnlyList<string> Permissions);

/// <summary>
/// Một mã quyền — schema <c>PermissionInfo</c>. <paramref name="Assignable"/> sai với đúng một mã, <c>role.manage</c> (L-D19): mã đó chỉ
/// ADMIN có (Đ-6.9), API không gán nó cho vai trò nào — FE vẽ ô đó khóa.
/// </summary>
public sealed record PermissionInfo(string Code, string? Description, bool Assignable);

/// <summary>Mã quyền gán được qua API và luật hình dạng mã vai trò — một chỗ cho ba validator.</summary>
public static class RoleRules
{
    public const int MaxDisplayNameLength = 50;

    /// <summary>
    /// 17 mã của <see cref="PermissionCodes.All"/> trừ <see cref="PermissionCodes.RoleManage"/> (L-D19, sửa 2026-09-24 khi thi công D5):
    /// Đ-6.9 nói <c>role.manage</c> "chỉ ADMIN có". Cho gán nó thì gắn nhầm vào USER là mọi người dùng sửa được vai trò — và với
    /// L-D18, nâng được bất kỳ ai lên ADMIN.
    /// </summary>
    public static readonly IReadOnlySet<string> AssignablePermissions =
        new HashSet<string>(PermissionCodes.All.Where(c => c != PermissionCodes.RoleManage), StringComparer.Ordinal);

    /// <summary>
    /// <c>^[A-Z][A-Z0-9_]{2,29}$</c> — schema <c>RoleCode</c>. So từng ký tự, không Regex: <c>$</c> của .NET khớp cả trước
    /// <c>"\n"</c> cuối chuỗi (bài học <c>VerifyEmailRequestValidator</c>).
    /// </summary>
    public static bool IsRoleCodeShape(string code) =>
        code.Length is >= 3 and <= 30
        && code[0] is >= 'A' and <= 'Z'
        && code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    public const string PermissionsInvalid = "Có mã quyền không tồn tại hoặc không gán được cho vai trò.";

    public static readonly string DisplayNameInvalid = $"Tên hiển thị phải từ 1 đến {MaxDisplayNameLength} ký tự.";

    public static bool IsDisplayName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaxDisplayNameLength;
}

/// <summary>
/// Body của <c>POST /admin/roles</c>. Class <c>init</c> + <c>[Required]</c> chỉ cho Swagger, cùng nếp mọi request khác. <c>code</c>
/// chỉ nhận ở ĐÂY — bất biến từ lúc tạo (Đ-6.9).
/// </summary>
public sealed class CreateRoleRequest
{
    [Required]
    public string? Code { get; init; }

    [Required]
    public string? DisplayName { get; init; }

    [Required]
    public IReadOnlyList<string>? Permissions { get; init; }
}

public sealed class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public const string CodeInvalid = "Mã vai trò gồm 3–30 ký tự A–Z, 0–9, _ và bắt đầu bằng chữ cái in hoa.";

    public const string CodeIsSystem = "Mã này thuộc vai trò hệ thống.";

    public const string PermissionsRequired = "Danh sách quyền là bắt buộc.";

    public CreateRoleRequestValidator()
    {
        RuleFor(x => x.Code)
            .Must(c => c is not null && RoleRules.IsRoleCodeShape(c))
            .WithMessage(CodeInvalid)
            .DependentRules(() => RuleFor(x => x.Code)
                .Must(c => !RoleCodes.All.Contains(c, StringComparer.Ordinal))
                .WithMessage(CodeIsSystem));

        RuleFor(x => x.DisplayName)
            .Must(RoleRules.IsDisplayName)
            .WithMessage(RoleRules.DisplayNameInvalid);

        RuleFor(x => x.Permissions)
            .NotNull()
            .WithMessage(PermissionsRequired)
            .DependentRules(() => RuleFor(x => x.Permissions)
                .Must(p => p!.All(RoleRules.AssignablePermissions.Contains))
                .WithMessage(RoleRules.PermissionsInvalid));
    }
}

/// <summary>
/// Body của <c>PATCH /admin/roles/{roleId}</c> — CHỈ <c>displayName</c> (Đ-6.9). Body có <c>code</c> hay trường lạ bất kỳ → 400 theo
/// tên trường, nhờ <c>UnmappedMemberHandling = Disallow</c> TOÀN CỤC của host (từ GĐ1) — không cần attribute riêng ở đây (cạm bẫy 1
/// của D5 giả định ngược lại; ghi ở "Thực tế thi công").
/// </summary>
public sealed class RenameRoleRequest
{
    [Required]
    public string? DisplayName { get; init; }
}

public sealed class RenameRoleRequestValidator : AbstractValidator<RenameRoleRequest>
{
    public RenameRoleRequestValidator()
    {
        RuleFor(x => x.DisplayName)
            .Must(RoleRules.IsDisplayName)
            .WithMessage(RoleRules.DisplayNameInvalid);
    }
}

/// <summary>
/// Body của <c>PUT /admin/roles/{roleId}/permissions</c> — thay CẢ tập (idempotent). <c>confirm</c> chỉ có nghĩa với USER/MODERATOR
/// (Đ-6.9): thiếu hay <c>false</c> → 409 <c>confirmation-required</c> TRƯỚC mọi ghi.
/// </summary>
public sealed class SetRolePermissionsRequest
{
    [Required]
    public IReadOnlyList<string>? Permissions { get; init; }

    public bool? Confirm { get; init; }
}

public sealed class SetRolePermissionsRequestValidator : AbstractValidator<SetRolePermissionsRequest>
{
    public SetRolePermissionsRequestValidator()
    {
        RuleFor(x => x.Permissions)
            .NotNull()
            .WithMessage(CreateRoleRequestValidator.PermissionsRequired)
            .DependentRules(() => RuleFor(x => x.Permissions)
                .Must(p => p!.All(RoleRules.AssignablePermissions.Contains))
                .WithMessage(RoleRules.PermissionsInvalid));
    }
}
