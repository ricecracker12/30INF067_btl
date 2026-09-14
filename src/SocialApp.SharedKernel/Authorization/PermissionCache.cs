using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Cache vai trò → tập quyền, TTL 60 giây, không đẩy invalidate (GĐ1 chưa sửa quyền lúc runtime — GĐ6).
/// KHÔNG liên quan tới thu hồi token khi đổi vai trò của user (Mục 7.5 "Đừng nhầm với cache quyền").
///
/// Key là claim role của token ĐÃ verify chữ ký, nên kẻ tấn công không đẻ ra được key tùy ý làm phình bộ
/// nhớ. Đừng tái dùng lớp này với key lấy từ query string.
/// </summary>
public sealed class PermissionCache(IServiceScopeFactory scopes, TimeProvider time) : IPermissionCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async ValueTask<IReadOnlySet<string>> GetAsync(string roleCode, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        if (_entries.TryGetValue(roleCode, out var hit) && hit.ExpiresAt > now)
            return hit.Permissions;

        // Cache là singleton, nguồn thật dùng DbContext (scoped) → tự mở scope. Inject thẳng nguồn
        // vào đây là captive dependency: một DbContext sống suốt đời app, dùng chung giữa các thread.
        await using var scope = scopes.CreateAsyncScope();
        var permissions = await scope.ServiceProvider
            .GetRequiredService<IRolePermissionSource>()
            .GetPermissionsAsync(roleCode, ct);

        // Nguồn ném thì KHÔNG tới được dòng này → không ghi entry → lỗi thành 500 và lần sau thử lại.
        // Cache cả lỗi (lưu tập rỗng) thì suốt 60 giây mọi Moderator bị 403 dù DB đã sống lại. Fail-closed.
        _entries[roleCode] = new Entry(permissions, now + Ttl);
        return permissions;
    }

    private sealed record Entry(IReadOnlySet<string> Permissions, DateTimeOffset ExpiresAt);
}
