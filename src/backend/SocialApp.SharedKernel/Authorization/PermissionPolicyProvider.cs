using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Dựng policy <c>perm:&lt;mã&gt;</c> lúc cần, để không phải đăng ký tay 17 policy cùng mọi quyền thêm ở GĐ
/// sau. Mọi tên policy khác — và policy mặc định, fallback policy — rơi về
/// <see cref="DefaultAuthorizationPolicyProvider"/>.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _default = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // GĐ6 C4: "perm-any:a|b|c" của [RequireAnyPermission]. Kiểm TRƯỚC "perm:" — hai tiền tố không chồng nhau ("perm-" ≠
        // "perm:"), nhưng thứ tự rõ ràng thì người đọc sau khỏi phải tự chứng minh điều đó.
        if (policyName.StartsWith(RequireAnyPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var permissions = policyName[RequireAnyPermissionAttribute.PolicyPrefix.Length..]
                .Split(RequireAnyPermissionAttribute.Separator);
            return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new AnyPermissionRequirement(permissions))
                .Build());
        }

        if (!policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return _default.GetPolicyAsync(policyName);

        var permission = policyName[RequirePermissionAttribute.PolicyPrefix.Length..];
        return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build());
    }

    // Hai dòng dưới KHÔNG phải thủ tục: provider thay thế provider mặc định trong DI, nên nếu tự trả
    // null ở đây thì FallbackPolicy của C4 biến mất — default deny tắt câm, không lỗi, không log.
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _default.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _default.GetFallbackPolicyAsync();
}
