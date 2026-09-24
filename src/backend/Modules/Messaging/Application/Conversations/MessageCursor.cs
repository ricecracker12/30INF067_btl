using System.Globalization;

namespace SocialApp.Modules.Messaging.Application.Conversations;

/// <summary>
/// Con trỏ của lịch sử tin (Mục 7.6): <c>base64url(seq)</c> của tin CŨ NHẤT trên trang vừa trả — trang kế lấy tin có
/// <c>seq &lt; Seq</c>. Mờ với client (Đ-2.11); <c>seq</c> không bí mật nhưng client không được dựa vào hình dạng cursor.
/// </summary>
public readonly record struct MessageCursor(long Seq)
{
    public string Encode() => Base64Url.Encode("s" + Seq.ToString(CultureInfo.InvariantCulture));

    public static bool TryDecode(string? raw, out MessageCursor cursor)
    {
        cursor = default;
        if (!Base64Url.TryDecode(raw, out var text)
            || text.Length < 2 || text[0] != 's'
            || !long.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var seq)
            || seq < 1)
            return false;

        cursor = new MessageCursor(seq);
        return true;
    }
}
