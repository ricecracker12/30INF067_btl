using System.Net;
using SocialApp.SharedKernel.Ids;

namespace SocialApp.Modules.Identity.Domain;

/// <summary>
/// Một refresh token đã phát (ENT-11, bảng <c>refresh_tokens</c>). <see cref="TokenHash"/> là
/// SHA-256 hex của bản rõ — bản rõ chỉ tồn tại trong response và trong cookie của client, KHÔNG
/// bao giờ nằm trong DB hay log.
///
/// <see cref="FamilyId"/>: mọi token sinh ra từ một lần đăng nhập mang cùng FamilyId. Reuse
/// detection thu hồi NGUYÊN FAMILY chứ không chỉ token bị dùng lại (Mục 3.5) — nếu chỉ thu hồi một
/// cái, kẻ trộm vẫn giữ được nhánh còn lại.
/// <see cref="ReplacedById"/>: dấu vết ai thay ai, để điều tra sau sự cố. Hai cột phục vụ hai mục
/// đích khác nhau, giữ cả hai.
/// </summary>
public sealed class RefreshToken
{
    /// <summary>Khóa chính UUID v7.</summary>
    public Guid Id { get; init; } = Uuid7.New();

    /// <summary>FK → <c>users.user_id</c>, ON DELETE CASCADE.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Định danh chuỗi xoay vòng, chung cho mọi token của một phiên đăng nhập (Mục 3.5).</summary>
    public required Guid FamilyId { get; init; }

    /// <summary>SHA-256 hex của bản rõ, <c>varchar(64)</c>, unique.</summary>
    public required string TokenHash { get; init; }

    /// <summary>Hạn dùng.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Thời điểm bị thu hồi; <c>null</c> nghĩa là còn hiệu lực — cũng là điều kiện của index một phần <c>idx_refresh_family</c>.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Token đã thay thế token này (self-FK → <c>refresh_tokens.id</c>). Dấu vết để điều tra.</summary>
    public Guid? ReplacedById { get; set; }

    /// <summary>IP lúc phát token. Kiểu BCL <see cref="IPAddress"/> — Npgsql ánh xạ thẳng sang <c>inet</c>, không cần kiểu riêng của EF.</summary>
    public IPAddress? CreatedIp { get; init; }

    /// <summary>Thời điểm phát.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
