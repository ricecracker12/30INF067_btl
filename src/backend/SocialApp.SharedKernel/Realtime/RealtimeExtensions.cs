using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialApp.SharedKernel.Redis;

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
    public static ISignalRServerBuilder AddSharedKernelRealtime(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(RedisConnection)))
            throw new InvalidOperationException(
                $"Gọi {nameof(RedisExtensions.AddSharedKernelRedis)} trước {nameof(AddSharedKernelRealtime)}.");

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<RealtimeOptions>().BindConfiguration(RealtimeOptions.Section);
        services.AddSingleton<IRealtimeTicketStore, RedisRealtimeTicketStore>();
        services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();

        return services
            .AddSignalR(o =>
            {
                o.AddFilter<RevocationHubFilter>();
                // Lỗi hub chỉ mang MÃ trong HubException.Message (Mục 8.2); không bao giờ gửi chi tiết exception ra client.
                o.EnableDetailedErrors = false;
            })
            .AddJsonProtocol(o =>
            {
                // Cùng quy ước JSON với REST (Program.cs): camelCase, enum là chuỗi thường ("delivered" | "seen").
                o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });
    }

    /// <summary>
    /// Thêm scheme <see cref="RealtimeTicketDefaults.Scheme"/> vào builder xác thực của host. KHÔNG đổi default scheme — bearer vẫn
    /// là mặc định cho REST; chỉ hub khai scheme này.
    /// </summary>
    public static AuthenticationBuilder AddRealtimeTicket(this AuthenticationBuilder builder) =>
        builder.AddScheme<AuthenticationSchemeOptions, RealtimeTicketAuthenticationHandler>(RealtimeTicketDefaults.Scheme, null);
}
