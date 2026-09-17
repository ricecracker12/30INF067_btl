using Microsoft.AspNetCore.Authorization;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Tầng 2 (Mục 6.2): khai quyền trên endpoint. Tên policy sinh động = tiền tố + mã quyền, được
/// <see cref="PermissionPolicyProvider"/> dựng lúc cần. Mã quyền dạng resource.action (Mục 5.2).
///
/// Nằm ở SharedKernel chứ không ở Identity: mọi module từ GĐ2 đều cần nó, và module không được tham chiếu
/// chéo. Gõ sai mã quyền vẫn compile — PermissionCodeUsageTests (ArchitectureTests) bắt chuyện đó.
/// </summary>
public sealed class RequirePermissionAttribute(string permission)
    : AuthorizeAttribute(PolicyPrefix + permission)
{
    public const string PolicyPrefix = "perm:";
    public string Permission { get; } = permission;
}
