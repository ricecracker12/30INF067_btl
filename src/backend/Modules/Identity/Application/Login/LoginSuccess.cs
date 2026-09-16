using SocialApp.Modules.Identity.Application.Security;

namespace SocialApp.Modules.Identity.Application.Login;

/// <summary>
/// Kết quả đăng nhập thành công. <paramref name="RefreshPlain"/> là bản rõ CHỈ để controller đặt vào cookie — không log,
/// không trả trong body (quyết định 6 của cổng mở).
/// </summary>
public sealed record LoginSuccess(AccessToken Access, string RefreshPlain);
