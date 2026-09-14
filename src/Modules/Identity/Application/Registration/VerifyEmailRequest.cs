using System.ComponentModel.DataAnnotations;

namespace SocialApp.Modules.Identity.Application.Registration;

/// <summary>
/// Khớp <c>VerifyEmailRequest</c> của hợp đồng. Cùng khuôn <see cref="RegisterRequest"/>: class với <c>[Required]</c> trên
/// property — Swagger ghi <c>required: [token]</c>, validate thật do <see cref="VerifyEmailRequestValidator"/>.
/// </summary>
public sealed class VerifyEmailRequest
{
    /// <summary>Token bản rõ lấy từ query string của link trong mail.</summary>
    [Required]
    public required string Token { get; init; }
}
