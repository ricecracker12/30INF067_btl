namespace SocialApp.SharedKernel.Authorization;

/// <summary>Vai trò → tập mã quyền, có cache. PermissionHandler (C2) đọc qua đây, không gọi thẳng nguồn.</summary>
public interface IPermissionCache
{
    ValueTask<IReadOnlySet<string>> GetAsync(string roleCode, CancellationToken ct = default);
}
