using System.Globalization;
using System.Text;

namespace SocialApp.Modules.SocialGraph.Application.Relationships;

/// <summary>
/// Con trỏ keyset của danh sách bạn và lời mời (L13): chép <c>PostCursor</c> của Content, đổi nghĩa hai trường
/// thành <c>(since, otherUserId)</c>. Bạn bè: <c>since</c> là <c>accepted_at</c>. Lời mời: <c>since</c> là
/// <c>created_at</c>. Cả hai sắp DESC, hòa thì <c>otherUserId</c> DESC.
///
/// Không import Content — <c>ModuleBoundaryTests</c> chặn. Opaque với client: base64url của
/// <c>"{since:O}|{otherUserId:D}"</c>. Không ký: hai giá trị đã nằm trên thẻ trả về.
/// </summary>
public readonly record struct FriendCursor(DateTimeOffset Since, Guid OtherUserId)
{
    /// <summary>
    /// .NET 8 chưa có <c>Base64Url</c>. Base64 thường có <c>+</c> và <c>/</c>, hai ký tự phải percent-encode trong
    /// query string; quên một chỗ là cursor hỏng sau một vòng URL.
    /// </summary>
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Since:O}|{OtherUserId:D}"))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <summary>
    /// Giải mã, không ném với bất kỳ đầu vào nào. Cursor là chuỗi client gửi lên: một exception ở đây là 500 từ
    /// một chuỗi người dùng sửa tay, trong khi hợp đồng đòi 400 <c>errors.cursor</c>.
    /// </summary>
    public static bool TryDecode(string? raw, out FriendCursor cursor)
    {
        cursor = default;

        if (string.IsNullOrEmpty(raw) || !TryDecodeBase64Url(raw, out var bytes))
            return false;

        var parts = Encoding.UTF8.GetString(bytes).Split('|');
        if (parts.Length != 2
            || !DateTimeOffset.TryParseExact(
                parts[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var since)
            || !Guid.TryParseExact(parts[1], "D", out var otherUserId))
            return false;

        // ToUniversalTime() bắt buộc: Npgsql từ chối DateTimeOffset có Offset khác 0 — 500 từ cursor sửa tay.
        cursor = new FriendCursor(since.ToUniversalTime(), otherUserId);
        return true;
    }

    private static bool TryDecodeBase64Url(string raw, out byte[] bytes)
    {
        var padded = raw.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };

        bytes = [];
        return padded.Length % 4 == 0 && TryFromBase64(padded, out bytes);
    }

    private static bool TryFromBase64(string padded, out byte[] bytes)
    {
        var buffer = new byte[padded.Length * 3 / 4];
        if (Convert.TryFromBase64String(padded, buffer, out var written))
        {
            bytes = buffer[..written];
            return true;
        }

        bytes = [];
        return false;
    }
}
