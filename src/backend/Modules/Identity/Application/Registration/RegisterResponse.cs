namespace SocialApp.Modules.Identity.Application.Registration;

/// <summary>Body 201 của <c>POST /auth/register</c>. <paramref name="UserId"/> là UUID v7.</summary>
public sealed record RegisterResponse(Guid UserId, string Email);
