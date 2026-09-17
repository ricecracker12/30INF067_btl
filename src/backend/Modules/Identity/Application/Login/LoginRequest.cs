using System.ComponentModel.DataAnnotations;

namespace SocialApp.Modules.Identity.Application.Login;

/// <summary>
/// Khớp <c>LoginRequest</c> của hợp đồng. Cùng khuôn RegisterRequest: class với <c>[Required]</c> trên property — Swagger
/// ghi <c>required: [email, password]</c>, validate thật do <see cref="LoginRequestValidator"/>; không dùng từ khóa
/// <c>required</c> (xem RegisterRequest).
/// </summary>
public sealed class LoginRequest
{
    [Required]
    public string Email { get; init; } = "";

    [Required]
    public string Password { get; init; } = "";
}
