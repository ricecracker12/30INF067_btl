namespace SocialApp.Modules.Messaging.Domain;

/// <summary>
/// Luật nội dung tin nhắn — hàm thuần để unit test không cần DB (khuôn <c>PostContentPolicy</c> của GĐ2). Lưới cuối là
/// <c>ck_messages_content</c> của DB; service kiểm TRƯỚC để trả 400 có nghĩa thay vì 500 từ <c>23514</c>.
///
/// Đếm <b>ký tự Unicode</b> (rune), không đếm <c>string.Length</c>: <c>varchar(2000)</c> và <c>char_length</c> của Postgres đếm
/// code point, còn <c>Length</c> của .NET đếm UTF-16 — một emoji là 2. Đếm bằng <c>Length</c> là từ chối oan tin 1.500 emoji
/// mà DB nhận được; FE đếm bằng <c>[...s].length</c> (code point) nên hai bên cùng một con số.
///
/// Chỉ toàn khoảng trắng (kể cả xuống dòng, tab) → không hợp lệ. <c>btrim</c> của Postgres chỉ bỏ dấu cách nên chặt hơn DB ở
/// chỗ này là cố ý: một tin chỉ có hai dấu xuống dòng không phải tin.
/// </summary>
public static class MessageContentPolicy
{
    public const int MaxLength = 2000;

    public const string ContentKey = "content";

    public const string Empty = "Tin nhắn không được để trống.";

    // static readonly chứ không const: nội suy một hằng số nguyên vào chuỗi const là CS0133 (cùng lý do PostContentPolicy).
    public static readonly string TooLong = $"Tin nhắn không được vượt quá {MaxLength} ký tự.";

    /// <summary>Thông điệp lỗi, hoặc <c>null</c> khi hợp lệ.</summary>
    public static string? Validate(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Empty;

        return CountCharacters(content) > MaxLength ? TooLong : null;
    }

    /// <summary>Số code point — cùng cách đếm với <c>char_length</c> của Postgres.</summary>
    public static int CountCharacters(string content) => content.EnumerateRunes().Count();
}
