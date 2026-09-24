using Microsoft.AspNetCore.Authorization;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Tầng 2 "có BẤT KỲ quyền nào trong danh sách" (giai-doan-6.md Mục 6.1, GĐ6 C4). Dùng cho màn danh sách tài khoản: người chỉ có
/// <c>role.assign</c> cũng phải xem được danh sách để gán vai trò, không cần <c>user.lock</c>.
///
/// Tên policy mã hóa danh sách (<c>perm-any:a|b|c</c>), dựng lúc cần ở <see cref="PermissionPolicyProvider"/> — cùng cách
/// <see cref="RequirePermissionAttribute"/>. Gõ sai mã vẫn compile: <c>PermissionCodeUsageTests</c> đọc cả attribute này.
/// </summary>
public sealed class RequireAnyPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm-any:";
    public const char Separator = '|';

    public RequireAnyPermissionAttribute(params string[] permissions)
        : base(PolicyPrefix + string.Join(Separator, Validate(permissions)))
    {
        Permissions = permissions;
    }

    public IReadOnlyList<string> Permissions { get; }

    private static string[] Validate(string[] permissions)
    {
        if (permissions.Length < 2)
            throw new ArgumentException("Dùng [RequirePermission] khi chỉ có một mã quyền.", nameof(permissions));
        if (permissions.Any(p => string.IsNullOrWhiteSpace(p) || p.Contains(Separator)))
            throw new ArgumentException($"Mã quyền rỗng hoặc chứa '{Separator}'.", nameof(permissions));
        return permissions;
    }
}

/// <summary>Đạt khi vai trò có ít nhất một mã trong <see cref="Permissions"/>.</summary>
public sealed class AnyPermissionRequirement(IReadOnlyList<string> permissions) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; } = permissions;
}

/// <summary>
/// Handler của <see cref="AnyPermissionRequirement"/> — mỗi mã đi qua <see cref="PermissionChecks.IsAllowedAsync"/>, nên Admin
/// short-circuit và tra cache giống hệt <see cref="PermissionHandler"/>. Không <c>ctx.Fail()</c>: mặc định deny.
/// </summary>
public sealed class AnyPermissionHandler(IPermissionCache cache) : AuthorizationHandler<AnyPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext ctx, AnyPermissionRequirement req)
    {
        var role = ctx.User.FindFirst(Authentication.JwtClaims.Role)?.Value;
        foreach (var permission in req.Permissions)
        {
            if (await cache.IsAllowedAsync(role, permission))
            {
                ctx.Succeed(req);
                return;
            }
        }
    }
}
