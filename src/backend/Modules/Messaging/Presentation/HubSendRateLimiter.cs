using System.Threading.RateLimiting;

namespace SocialApp.Modules.Messaging.Presentation;

/// <summary>
/// Giới hạn tần suất <c>SendMessage</c> qua hub (C3): 60 lượt/phút/user, trong bộ nhớ của MỘT instance. Rate limiter HTTP không
/// chạm được lời gọi hub (chỉ lần bắt tay đi qua middleware). Đủ cho người thật, chặn script; vượt → mã <c>rate-limited</c>.
/// Với hai instance mỗi instance đếm riêng — chấp nhận: mục đích là chặn vòng lặp, không phải hạn mức chính xác.
/// </summary>
public sealed class HubSendRateLimiter : IDisposable
{
    public const int PermitsPerMinute = 60;

    private readonly PartitionedRateLimiter<Guid> _limiter = PartitionedRateLimiter.Create<Guid, Guid>(userId =>
        RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PermitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    public bool TryAcquire(Guid userId)
    {
        using var lease = _limiter.AttemptAcquire(userId);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
