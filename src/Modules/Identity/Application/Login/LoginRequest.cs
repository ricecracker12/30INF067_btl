using System.ComponentModel.DataAnnotations;

namespace SocialApp.Modules.Identity.Application.Login;

/// <summary>
/// Khớp <c>LoginRequest</c> của hợp đồng. Cùng khuôn RegisterRequest: class với <c>[Required]</c> trên property — Swagger
/// ghi <c>required: [email, password]</c>, validate thật do <see cref="LoginRequestValidator"/>.
/// </summary>
public sealed class LoginRequest
{
    [Required]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}
