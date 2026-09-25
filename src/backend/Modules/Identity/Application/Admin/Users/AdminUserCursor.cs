using System.Globalization;
using System.Text;

namespace SocialApp.Modules.Identity.Application.Admin.Users;

/// <summary>
/// Con trỏ keyset của <c>GET /admin/users</c>: chép khuôn <c>PostCursor</c> của Content (hướng dẫn khối D GĐ6, D2 bước 3), hai
/// trường là <c>(created_at, user_id)</c> của tài khoản CUỐI trang trước. Sắp <c>created_at DESC</c>, hòa thì <c>user_id DESC</c> —
/// khóa phụ là bắt buộc: tài khoản seed cùng một mili giây mà thiếu nó thì trùng/sót giữa hai trang (cạm bẫy 4 của D2).
///
/// Không import Content — <c>ModuleBoundaryTests</c> chặn. Opaque với client: base64url của <c>"{createdAt:O}|{userId:D}"</c>.
/// Không ký: hai giá trị đã nằm trên <c>AdminUser</c> trả về.
/// </summary>
public readonly record struct AdminUserCursor(DateTimeOffset CreatedAt, Guid UserId)
{
    /// <summary>
    /// .NET 8 chưa có <c>Base64Url</c>. Base64 thường có <c>+</c> và <c>/</c>, hai ký tự phải percent-encode trong query string;
    /// quên một chỗ là cursor hỏng sau một vòng URL.
    /// </summary>
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CreatedAt:O}|{UserId:D}"))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <summary>
    /// Giải mã, không ném với bất kỳ đầu vào nào. Cursor là chuỗi client gửi lên: một exception ở đây là 500 từ một chuỗi người
    /// dùng sửa tay, trong khi hợp đồng đòi 400 <c>errors.cursor</c>.
    /// </summary>
    public static bool TryDecode(string? raw, out AdminUserCursor cursor)
    {
        cursor = default;

        if (string.IsNullOrEmpty(raw) || !TryDecodeBase64Url(raw, out var bytes))
            return false;

        var parts = Encoding.UTF8.GetString(bytes).Split('|');
        if (parts.Length != 2
            || !DateTimeOffset.TryParseExact(
                parts[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt)
            || !Guid.TryParseExact(parts[1], "D", out var userId))
            return false;

        // ToUniversalTime() bắt buộc: Npgsql từ chối DateTimeOffset có Offset khác 0 — 500 từ cursor sửa tay.
        cursor = new AdminUserCursor(createdAt.ToUniversalTime(), userId);
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
