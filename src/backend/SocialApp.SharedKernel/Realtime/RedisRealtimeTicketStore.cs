using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SocialApp.SharedKernel.Redis;
using StackExchange.Redis;

namespace SocialApp.SharedKernel.Realtime;

/// <summary>
/// Vé trong Redis (Đ-5.9): <c>SET rt:ticket:{sha256(vé)} = "{expiresAtUnixMs}|{iat}|{role}|{sub}" EX 30</c>, đổi bằng <c>GETDEL</c>.
///
/// Hạn kiểm HAI chỗ: TTL của Redis dọn khóa, còn <c>expiresAt</c> trong giá trị được so với <see cref="TimeProvider"/> lúc đổi
/// vé — để test HUB-03 ("vé quá 30 giây") chạy bằng đồng hồ giả, không phải ngủ 30 giây.
///
/// Không lưu vé thô, không log vé. Log chỉ nói "không cấp được vé" — không kèm danh tính.
/// </summary>
internal sealed class RedisRealtimeTicketStore(
    RedisConnection redis,
    TimeProvider clock,
    ILogger<RedisRealtimeTicketStore> logger) : IRealtimeTicketStore
{
    public const string KeyPrefix = "rt:ticket:";

    public async Task<RealtimeTicketIssue?> IssueAsync(RealtimeTicketClaims claims, CancellationToken ct = default)
    {
        if (await ConnectedOrNullAsync(ct) is not { } connection)
            return null;

        var ticket = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = clock.GetUtcNow().Add(RealtimeTicketDefaults.Lifetime).ToUnixTimeMilliseconds();
        var value = string.Create(CultureInfo.InvariantCulture, $"{expiresAt}|{claims.Iat}|{claims.Role}|{claims.Sub}");

        try
        {
            await connection.GetDatabase().StringSetAsync(Key(ticket), value, RealtimeTicketDefaults.Lifetime);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis không sẵn sàng — không cấp được vé realtime (client chuyển fallback REST)");
            return null;
        }

        return new RealtimeTicketIssue(ticket, (int)RealtimeTicketDefaults.Lifetime.TotalSeconds);
    }

    public async Task<RealtimeTicketClaims?> RedeemAsync(string ticket, CancellationToken ct = default)
    {
        // Vé là 32 byte base64url = 43 ký tự. Chặn chuỗi rác trước khi chạm Redis — không để một query dài tùy ý thành khóa.
        if (ticket.Length is < 40 or > 64 || await ConnectedOrNullAsync(ct) is not { } connection)
            return null;

        RedisValue value;
        try
        {
            value = await connection.GetDatabase().StringGetDeleteAsync(Key(ticket));
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis không sẵn sàng — không đổi được vé realtime");
            return null;
        }

        if (value.IsNullOrEmpty)
            return null;

        var parts = value.ToString().Split('|', 4);
        if (parts.Length != 4
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iat)
            || clock.GetUtcNow().ToUnixTimeMilliseconds() > expiresAt)
            return null;

        return new RealtimeTicketClaims(parts[3], parts[2], iat);
    }

    /// <summary>
    /// CHỜ lần kết nối đầu có kết quả (tối đa <c>ConnectTimeout</c>), không dùng <see cref="RedisConnection.ConnectedOrNull"/>: cấp/đổi
    /// vé không nằm trên mọi request như kiểm thu hồi, và không chờ thì request đầu ngay sau khi host khởi động nhận 503 oan dù
    /// Redis sống (lộ ở <c>BackplaneTests</c> trên CI — host thứ hai xin vé khi kết nối nền chưa xong). Redis chết thì chỉ lần đầu
    /// chờ, sau đó task đã xong và trả null ngay.
    /// </summary>
    private async Task<IConnectionMultiplexer?> ConnectedOrNullAsync(CancellationToken ct)
    {
        var connection = await redis.GetAsync().WaitAsync(ct);
        return connection.IsConnected ? connection : null;
    }

    private static string Key(string ticket) =>
        KeyPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ticket))).ToLowerInvariant();

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
