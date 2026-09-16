using SocialApp.Modules.Identity.Application.Security;

namespace SocialApp.Modules.Identity.Application.Session;

/// <summary>
/// Cặp token mới sau refresh. <paramref name="RefreshPlain"/> là bản rõ CHỈ để controller đặt vào cookie — không log, không
/// trả trong body.
/// </summary>
public sealed record RefreshSuccess(AccessToken Access, string RefreshPlain);
