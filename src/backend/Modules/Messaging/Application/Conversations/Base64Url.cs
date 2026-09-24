using System.Text;

namespace SocialApp.Modules.Messaging.Application.Conversations;

/// <summary>
/// base64url cho hai cursor của module (.NET 8 chưa có <c>Base64Url</c>). Chép từ <c>FriendCursor</c> của SocialGraph — không
/// import được. Base64 thường có <c>+</c> và <c>/</c> phải percent-encode trong query string; quên một chỗ là cursor hỏng.
/// </summary>
internal static class Base64Url
{
    public static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Không ném với bất kỳ đầu vào nào — cursor là chuỗi client gửi lên (400, không phải 500).</summary>
    public static bool TryDecode(string? raw, out string value)
    {
        value = "";
        if (string.IsNullOrEmpty(raw) || raw.Length > 256)
            return false;

        var padded = raw.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        if (padded.Length % 4 != 0)
            return false;

        var buffer = new byte[padded.Length * 3 / 4];
        if (!Convert.TryFromBase64String(padded, buffer, out var written))
            return false;

        try
        {
            value = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(buffer, 0, written);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
