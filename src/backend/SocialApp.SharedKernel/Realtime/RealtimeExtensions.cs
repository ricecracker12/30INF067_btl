using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialApp.SharedKernel.Redis;
using StackExchange.Redis;

namespace SocialApp.SharedKernel.Realtime;

/// <summary>
/// Điểm ráp realtime DÙNG CHUNG cho mọi hub (giai-doan-5.md Đ-5.8, Đ-5.9, Đ-5.10; GĐ6 Đ-6.18 dùng lại nguyên): vé, scheme,
/// <see cref="IUserIdProvider"/> đọc <c>sub</c>, filter thu hồi + tuổi thọ, giao thức JSON. Module có hub chỉ việc
/// <c>MapHub</c> ở host và khai <c>[Authorize(AuthenticationSchemes = RealtimeTicketDefaults.Scheme)]</c>.
/// </summary>
public static class RealtimeExtensions
{
    /// <summary>
    /// SignalR + kho vé + <see cref="SubClaimUserIdProvider"/> + <see cref="RevocationHubFilter"/> toàn cục. Đòi
    /// <see cref="RedisConnection"/> đã đăng ký (<c>AddSharedKernelRedis</c>) — thiếu thì chết ngay lúc khởi động.
    /// </summary>
    public static ISignalRServerBuilder AddSharedKernelRealtime(this IServiceCollection services, RealtimeBackplane? backplane = null)
    {
        if (!services.Any(d => d.ServiceType == typeof(RedisConnection)))
            throw new InvalidOperationException(
                $"Gọi {nameof(RedisExtensions.AddSharedKernelRedis)} trước {nameof(AddSharedKernelRealtime)}.");

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<RealtimeOptions>().BindConfiguration(RealtimeOptions.Section);
        services.AddSingleton<IRealtimeTicketStore, RedisRealtimeTicketStore>();
        services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();

        // C5 (Đ-5.11): presence — MỘT tracker vừa là IPresenceReader (GĐ6 đọc) vừa là BackgroundService gia hạn kết nối cục bộ.
        services.AddSingleton<RedisPresenceTracker>();
        services.AddSingleton<IPresenceReader>(sp => sp.GetRequiredService<RedisPresenceTracker>());
        services.AddHostedService(sp => sp.GetRequiredService<RedisPresenceTracker>());

        var signalR = services
            .AddSignalR(o =>
            {
                o.AddFilter<RevocationHubFilter>();
                o.AddFilter<PresenceHubFilter>();
                // Lỗi hub chỉ mang MÃ trong HubException.Message (Mục 8.2); không bao giờ gửi chi tiết exception ra client.
                o.EnableDetailedErrors = false;
            })
            .AddJsonProtocol(o =>
            {
                // Cùng quy ước JSON với REST (Program.cs): camelCase, enum là chuỗi thường ("delivered" | "seen").
                o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });

        // C4 (Đ-5.13, ADR-003): backplane Redis CHỈ khi bật — một instance thì nó chỉ thêm một điểm hỏng (Redis chết = mọi lần đẩy
        // chết, kể cả tới kết nối cùng instance). Kết nối RIÊNG, không dùng RedisConnection chung: kết nối chung có SyncTimeout =
        // AsyncTimeout = 250 ms (RedisExtensions) — hợp cho kiểm thu hồi, quá ngắn cho pub/sub. ChannelPrefix theo môi trường để
        // staging và production chung một Redis không nghe lẫn nhau.
        if (backplane is { Enabled: true })
            signalR.AddStackExchangeRedis(backplane.ConnectionString, o =>
            {
                o.Configuration.ChannelPrefix = RedisChannel.Literal(backplane.ChannelPrefix);
                o.Configuration.AbortOnConnectFail = false;
            });

        return signalR;
    }

    /// <summary>
    /// Thêm scheme <see cref="RealtimeTicketDefaults.Scheme"/> vào builder xác thực của host. KHÔNG đổi default scheme — bearer vẫn
    /// là mặc định cho REST; chỉ hub khai scheme này.
    /// </summary>
    public static AuthenticationBuilder AddRealtimeTicket(this AuthenticationBuilder builder) =>
        builder.AddScheme<AuthenticationSchemeOptions, RealtimeTicketAuthenticationHandler>(RealtimeTicketDefaults.Scheme, null);
}

/// <summary>
/// Cấu hình backplane (C4). <see cref="Enabled"/> đọc từ <c>Realtime:Backplane:Enabled</c> (mặc định <c>false</c> — staging hiện một
/// bản sao); bật khi GĐ7 khối E chạy bản sao thứ hai. <see cref="ChannelPrefix"/> = <c>socialapp-{môi trường}</c>.
/// </summary>
public sealed record RealtimeBackplane(bool Enabled, string ConnectionString, string ChannelPrefix);
