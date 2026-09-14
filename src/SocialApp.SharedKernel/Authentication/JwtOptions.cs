namespace SocialApp.SharedKernel.Authentication;

/// <summary>
/// Cấu hình JWT — MỘT nguồn cho ba nơi: validate (Api, C4), phát token (Identity, D3), TTL của key
/// revoked:user (D8). AccessTokenSeconds đổi ở đây là cả ba đổi theo (Mục 7.5 "Vì sao TTL là 930 giây").
/// Chỉ SigningKey là bí mật: nằm ở biến môi trường / deploy/.env, không bao giờ trong appsettings.
///
/// Đặt ở SharedKernel (Đ2): Api (validate) và Identity (phát token) đều đã tham chiếu SharedKernel; để trong
/// Identity thì cấu hình validate của host phụ thuộc kiểu nội bộ của một module.
/// </summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public const int MinSigningKeyBytes = 32;   // HS256 cần khóa ≥ 256 bit

    public string SigningKey { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public int AccessTokenSeconds { get; init; } = 900;
}
