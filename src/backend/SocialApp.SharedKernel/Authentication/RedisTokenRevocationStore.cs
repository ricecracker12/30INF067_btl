using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocialApp.SharedKernel.Redis;
using StackExchange.Redis;

namespace SocialApp.SharedKernel.Authentication;

/// <summary>
/// <c>SET revoked:user:&lt;id&gt; &lt;unix giây&gt; EX AccessTokenSeconds + ClockSkewSeconds</c> (Mục 7.5). Một key cho mỗi user,
/// bất kể bao nhiêu thiết bị.
/// </summary>
internal sealed class RedisTokenRevocationStore(
    RedisConnection redis, IOptions<JwtOptions> jwt, ILogger<RedisTokenRevocationStore> logger)
    : ITokenRevocationStore
{
    public const string KeyPrefix = "revoked:user:";

    private static string Key(string userId) => KeyPrefix + userId;

    /// <summary>
    /// Đ-D4: key phải sống bằng thời gian dài nhất một token phát trước mốc còn được JwtBearer chấp nhận = TTL access + ClockSkew.
    /// Đọc CÙNG <see cref="JwtOptions"/> với phía phát và phía validate — không ghi số 930 ở đâu cả.
    /// </summary>
    private TimeSpan Ttl => TimeSpan.FromSeconds(jwt.Value.AccessTokenSeconds + JwtOptions.ClockSkewSeconds);

    public async Task RevokeUserAsync(Guid userId, DateTimeOffset at, CancellationToken ct = default)
    {
        var connection = await redis.GetAsync();
        await connection.GetDatabase().StringSetAsync(Key(userId.ToString()), at.ToUnixTimeSeconds(), Ttl);
    }

    public async Task<bool> IsRevokedAsync(string userId, long issuedAtUnix, CancellationToken ct = default)
    {
        // Chưa kết nối → fail-open NGAY, không chờ timeout: nếu không, mọi request có token chậm theo Redis chết (Đ-D8).
        // Quyết định có ý thức (Mục 7.5 "Khi Redis chết"): Redis sập không được kéo sập cả API; đổi lại việc thu hồi tạm mất tác
        // dụng, còn exp 15 phút vẫn giữ.
        if (redis.ConnectedOrNull() is not { } connection)
        {
            LogFailOpen(null);
            return false;
        }

        try
        {
            var value = await connection.GetDatabase().StringGetAsync(Key(userId));
            return value.TryParse(out long revokedAt) && issuedAtUnix < revokedAt;
        }
        // RedisTimeoutException KHÔNG kế thừa RedisException (nó là TimeoutException) — bắt riêng, không thì Redis chậm = 500.
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            LogFailOpen(ex);
            return false;
        }
    }

    private void LogFailOpen(Exception? ex) =>
        logger.LogWarning(ex, "Redis không sẵn sàng — bỏ qua kiểm tra thu hồi access token (fail-open, Mục 7.5)");
}
