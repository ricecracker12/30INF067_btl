using System.Globalization;

namespace SocialApp.Modules.Messaging.Application.Conversations;

/// <summary>
/// Con trỏ keyset của danh sách hội thoại: <c>(last_message_at, id)</c>, sắp DESC (Mục 8.1 <c>ConversationPage</c>). Mờ với
/// client (Đ-2.11): base64url của <c>"{lastMessageAt:O}|{id:D}"</c>. Không ký: hai giá trị đã nằm trên dòng trả về.
/// </summary>
public readonly record struct ConversationCursor(DateTimeOffset LastMessageAt, Guid Id)
{
    public string Encode() => Base64Url.Encode($"{LastMessageAt:O}|{Id:D}");

    public static bool TryDecode(string? raw, out ConversationCursor cursor)
    {
        cursor = default;
        if (!Base64Url.TryDecode(raw, out var text))
            return false;

        var parts = text.Split('|');
        if (parts.Length != 2
            || !DateTimeOffset.TryParseExact(
                parts[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            || !Guid.TryParseExact(parts[1], "D", out var id))
            return false;

        // ToUniversalTime() bắt buộc: Npgsql từ chối DateTimeOffset có Offset khác 0 — 500 từ cursor sửa tay.
        cursor = new ConversationCursor(at.ToUniversalTime(), id);
        return true;
    }
}
