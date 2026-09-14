using System.Security.Cryptography;
using System.Text;

namespace SocialApp.Modules.Identity.Application.Security;

/// <summary>
/// Token bản rõ gửi cho client (link xác minh, cookie refresh) và băm lưu DB. MỘT chỗ cho cả hai loại token:
/// hai cách băm là hai nguồn sự thật — verify-email so băm lệch thì mọi link đều 400 mà không có lỗi nào.
/// Bản rõ không bao giờ vào DB hay log (NFR-SEC-01).
/// </summary>
public static class SecureToken
{
    public const int ByteLength = 32;

    /// <summary>32 byte ngẫu nhiên, hex thường — 64 ký tự, an toàn trong URL và cookie.</summary>
    public static string Generate() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(ByteLength)).ToLowerInvariant();

    /// <summary>
    /// SHA-256 hex thường của byte UTF-8 của CHUỖI nhận được — khớp cột varchar(64). Không giải hex về 32 byte
    /// gốc rồi mới băm: lúc verify server chỉ có chuỗi (Đ-D2).
    /// </summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
