using Microsoft.AspNetCore.SignalR;
using SocialApp.SharedKernel.Authentication;

namespace SocialApp.SharedKernel.Realtime;

/// <summary>
/// <c>Context.UserIdentifier</c> = claim <c>sub</c> (Đ-5.8). <c>IUserIdProvider</c> mặc định của SignalR đọc
/// <c>ClaimTypes.NameIdentifier</c>, mà dự án tắt <c>MapInboundClaims</c> và dùng <c>sub</c> — quên đăng ký lớp này thì
/// <c>Clients.User(...)</c> IM LẶNG không gửi cho ai (HUB-09, HUB-20 bắt).
/// </summary>
public sealed class SubClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirst(JwtClaims.Sub)?.Value;
}
