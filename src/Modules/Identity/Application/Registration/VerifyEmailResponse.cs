namespace SocialApp.Modules.Identity.Application.Registration;

/// <summary>Body 200 của <c>POST /auth/verify-email</c>.</summary>
public sealed record VerifyEmailResponse(string Email, DateTimeOffset VerifiedAt);
