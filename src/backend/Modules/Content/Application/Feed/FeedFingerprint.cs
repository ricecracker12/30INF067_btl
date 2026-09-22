using System.Security.Cryptography;
using System.Text;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.Content.Application.Feed;

/// <summary>
/// Dấu nguồn của cache trang đầu (Q-C1, Đ-4.8): băm ngắn của hai tập <c>Friends</c> + <c>FollowingOnly</c>. Nguồn đổi (kết
/// bạn, hủy kết bạn, theo dõi) → dấu đổi → trang đầu đã cache bị coi là trượt, mà SocialGraph không cần biết khóa của
/// Content. Hàm thuần: nguồn hiện tại đã có trong tay trước khi thử cache (Mục 7.2 bước 2), nên không tốn truy vấn nào.
/// </summary>
public static class FeedFingerprint
{
    /// <summary>
    /// SHA-256 của hai mảng id đã sắp, cắt 16 byte, base64. Hai tập được TÁCH bằng nhãn riêng — cùng một người chuyển từ
    /// "bạn" sang "chỉ theo dõi" (hủy kết bạn khi vẫn theo dõi) phải đổi dấu, dù hợp của hai tập không đổi.
    /// </summary>
    public static string Of(FeedSources sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var text = new StringBuilder("f:");
        foreach (var id in sources.Friends.Order())
            text.Append(id.ToString("D")).Append(',');
        text.Append("|o:");
        foreach (var id in sources.FollowingOnly.Order())
            text.Append(id.ToString("D")).Append(',');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToBase64String(hash, 0, 16);
    }
}
