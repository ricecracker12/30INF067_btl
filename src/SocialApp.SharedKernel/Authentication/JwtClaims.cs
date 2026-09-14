namespace SocialApp.SharedKernel.Authentication;

/// <summary>
/// Tên claim theo Mục 6.1 / 6.7.3. Token mang tên NGẮN, không phải URI của ClaimTypes. Đặt ở SharedKernel
/// vì từ GĐ2 mọi module đọc "sub" để kiểm ownership mà không được tham chiếu Identity (Đ2).
/// </summary>
public static class JwtClaims
{
    public const string Sub = "sub";
    public const string Role = "role";
    public const string Iat = "iat";
    public const string Jti = "jti";
}
