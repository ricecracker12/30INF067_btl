namespace SocialApp.Modules.Identity.Application.Login;

/// <summary>Body 200 của <c>/auth/login</c> (và <c>/auth/refresh</c> ở D5). Refresh token KHÔNG nằm ở đây — nó đi trong cookie.</summary>
public sealed record TokenResponse(string AccessToken, int ExpiresIn);
