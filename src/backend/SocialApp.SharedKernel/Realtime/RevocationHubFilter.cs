using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SocialApp.SharedKernel.Authentication;

namespace SocialApp.SharedKernel.Realtime;

/// <summary>
/// Đ-5.10 — kết nối đang mở không được sống lâu hơn quyền của người mở nó. Filter TOÀN CỤC (mọi hub: <c>/hubs/chat</c> của GĐ5,
/// <c>/hubs/notifications</c> của GĐ6 — Đ-6.18), hai cơ chế:
///
/// 1. <b>Mỗi lời gọi phương thức</b> kiểm <c>revoked:user</c> với <c>iat</c> của vé. Bị thu hồi → <c>Context.Abort()</c> và
///    lời gọi không tới hub (HUB-04b). Redis không trả lời → cho qua (fail-open), cùng chính sách REST của GĐ1.
/// 2. <b>Tuổi thọ tối đa</b> = TTL access token (<see cref="JwtOptions.AccessTokenSeconds"/>, 15 phút): hết hạn thì server cắt,
///    client nối lại → xin vé mới → BFF phải còn phiên hợp lệ. Người bị khóa mà không gọi phương thức nào vẫn mất kết nối trong
///    tối đa 15 phút — cùng cửa sổ REST đang chấp nhận (HUB-10). Đồng hồ là <see cref="TimeProvider"/> để test không phải chờ.
/// </summary>
public sealed class RevocationHubFilter(
    ITokenRevocationStore revocation,
    TimeProvider clock,
    IOptions<JwtOptions> jwt,
    IOptions<RealtimeOptions> realtime) : IHubFilter
{
    private const string LifetimeTimerKey = "socialapp.realtime.lifetime";

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var context = invocationContext.Context;
        if (await IsRevokedAsync(context))
        {
            context.Abort();
            throw new HubException("unauthorized");
        }

        return await next(invocationContext);
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var caller = context.Context;
        var timer = clock.CreateTimer(
            static state => ((HubCallerContext)state!).Abort(),
            caller,
            realtime.Value.MaxConnectionLifetime ?? TimeSpan.FromSeconds(jwt.Value.AccessTokenSeconds),
            Timeout.InfiniteTimeSpan);
        caller.Items[LifetimeTimerKey] = timer;

        await next(context);
    }

    public async Task OnDisconnectedAsync(
        HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        if (context.Context.Items.TryGetValue(LifetimeTimerKey, out var timer) && timer is IDisposable disposable)
            disposable.Dispose();

        await next(context, exception);
    }

    private async Task<bool> IsRevokedAsync(HubCallerContext context)
    {
        var sub = context.User?.FindFirst(JwtClaims.Sub)?.Value;
        var iatText = context.User?.FindFirst(JwtClaims.Iat)?.Value;
        if (sub is null || !long.TryParse(iatText, NumberStyles.None, CultureInfo.InvariantCulture, out var iat))
            return true;   // principal không đến từ vé — không bao giờ xảy ra khi hub khai đúng scheme; từ chối cho chắc

        return await revocation.CheckAsync(sub, iat, context.ConnectionAborted) == RevocationCheck.Revoked;
    }
}
