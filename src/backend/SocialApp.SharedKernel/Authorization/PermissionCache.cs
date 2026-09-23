using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Cache vai trò → tập quyền, TTL 60 giây. Từ GĐ6 có invalidate đẩy (Đ-6.10): <see cref="Invalidate"/> tại chỗ + pub/sub cho
/// instance khác (<see cref="IPermissionChangeNotifier"/>); TTL chỉ còn là lưới cuối khi Redis chết.
/// KHÔNG liên quan tới thu hồi token khi đổi vai trò của user (Mục 7.5 "Đừng nhầm với cache quyền").
///
/// Key là claim role của token ĐÃ verify chữ ký, nên kẻ tấn công không đẻ ra được key tùy ý làm phình bộ
/// nhớ. Đừng tái dùng lớp này với key lấy từ query string.
///
/// <b>Thế hệ</b> (L-C5 của hướng dẫn khối A+C GĐ6): request R đọc nguồn lúc T0 (quyền cũ), Admin commit + <see cref="Invalidate"/>
/// lúc T1, R ghi entry lúc T2 → không có thế hệ thì cache giữ quyền cũ thêm 60 giây dù đã invalidate. Mỗi lần nạp nhớ thế hệ lúc
/// BẮT ĐẦU; thế hệ đổi trong lúc nạp thì kết quả vẫn trả cho request đó nhưng KHÔNG ghi vào cache.
/// </summary>
public sealed class PermissionCache(IServiceScopeFactory scopes, TimeProvider time) : IPermissionCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _generations = new(StringComparer.Ordinal);
    private long _globalGeneration;

    public async ValueTask<IReadOnlySet<string>> GetAsync(string roleCode, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        if (_entries.TryGetValue(roleCode, out var hit) && hit.ExpiresAt > now)
            return hit.Permissions;

        var generation = Generation(roleCode);

        // Cache là singleton, nguồn thật dùng DbContext (scoped) → tự mở scope. Inject thẳng nguồn
        // vào đây là captive dependency: một DbContext sống suốt đời app, dùng chung giữa các thread.
        await using var scope = scopes.CreateAsyncScope();
        var permissions = await scope.ServiceProvider
            .GetRequiredService<IRolePermissionSource>()
            .GetPermissionsAsync(roleCode, ct);

        // Nguồn ném thì KHÔNG tới được dòng này → không ghi entry → lỗi thành 500 và lần sau thử lại.
        // Cache cả lỗi (lưu tập rỗng) thì suốt 60 giây mọi Moderator bị 403 dù DB đã sống lại. Fail-closed.
        // Bị invalidate trong lúc nạp → kết quả này có thể đọc trước COMMIT của thay đổi, không được nằm lại trong cache. Ghi TRƯỚC
        // rồi kiểm thế hệ SAU: Invalidate chen vào giữa "so" và "ghi" thì so-rồi-ghi vẫn để lại entry cũ; ghi-rồi-so thì hoặc
        // Invalidate xóa sau khi ta ghi, hoặc ta thấy thế hệ đổi và tự gỡ ĐÚNG entry mình vừa ghi (không đụng entry mới hơn).
        var entry = new Entry(permissions, now + Ttl);
        _entries[roleCode] = entry;
        if (Generation(roleCode) != generation)
            _entries.TryRemove(new KeyValuePair<string, Entry>(roleCode, entry));

        return permissions;
    }

    public void Invalidate(string roleCode)
    {
        // Tăng thế hệ TRƯỚC khi xóa: lần nạp đang chạy thấy thế hệ đổi và bỏ kết quả, dù nó ghi sau dòng TryRemove.
        _generations.AddOrUpdate(roleCode, 1, static (_, g) => g + 1);
        _entries.TryRemove(roleCode, out _);
    }

    public void InvalidateAll()
    {
        Interlocked.Increment(ref _globalGeneration);
        _entries.Clear();
    }

    /// <summary>Thế hệ hiệu lực của một vai trò = thế hệ riêng + thế hệ toàn cục (đổi ở <see cref="InvalidateAll"/>).</summary>
    private long Generation(string roleCode) =>
        Volatile.Read(ref _globalGeneration) + (_generations.TryGetValue(roleCode, out var g) ? g : 0);

    private sealed record Entry(IReadOnlySet<string> Permissions, DateTimeOffset ExpiresAt);
}
