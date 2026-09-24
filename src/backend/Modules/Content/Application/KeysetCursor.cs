using System.Globalization;
using System.Text;

namespace SocialApp.Modules.Content.Application;

/// <summary>
/// Con trỏ phân trang keyset dùng chung của module (Đ-2.11, tách ở D0 GĐ3 — Đ-3.6): vị trí của dòng CUỐI trang trước theo khóa
/// sắp <c>(created_at, id)</c>. Không biết chiều sắp — danh sách bài dùng DESC, danh sách bình luận dùng ASC; chiều là việc của
/// câu truy vấn, con trỏ chỉ ghi lại vị trí.
///
/// <b>Opaque với client</b>: mã hóa base64url của <c>"{created_at:O}|{id:D}"</c>. FE chỉ chuyển tiếp <c>nextCursor</c> nhận được,
/// không tự dựng — và vì nó opaque nên đổi cách mã hóa ở GĐ sau không phải đổi hợp đồng. Không ký, không mã hóa: nội dung là hai
/// giá trị vốn đã nằm trong response, giấu đi không được gì.
///
/// Vì sao keyset chứ không <c>OFFSET</c>: dòng mới chèn vào giữa hai lần gọi sẽ đẩy mọi thứ đi một dòng, và trang 2 theo offset
/// sẽ lặp lại dòng cuối của trang 1 (nhân đôi) — hoặc bỏ sót nếu có dòng bị xóa (nhảy cóc). Keyset neo vào một VỊ TRÍ chứ không
/// vào một SỐ ĐẾM nên không có hai lỗi đó (<c>PAGE-03</c>).
/// </summary>
public readonly record struct KeysetCursor(DateTimeOffset CreatedAt, Guid Id)
{
    /// <summary>
    /// .NET 8 chưa có <c>Base64Url</c> (bài học của GĐ1) — tự thay ký tự và bỏ <c>'='</c>. Base64 thường có <c>+</c> và
    /// <c>/</c>, hai ký tự phải percent-encode trong query string; quên một chỗ là cursor hỏng sau một vòng URL.
    /// </summary>
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CreatedAt:O}|{Id:D}"))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <summary>
    /// Giải mã, KHÔNG NÉM với bất kỳ đầu vào nào. Cursor là chuỗi client gửi lên nên phải coi là dữ liệu thù địch: một
    /// exception ở đây là <b>500 từ một chuỗi người dùng sửa tay</b>, trong khi hợp đồng đòi <b>400</b>
    /// <c>errors.cursor</c> (<c>PAGE-02</c>).
    /// </summary>
    /// <returns><c>false</c> cho mọi loại rác; <c>true</c> kèm <paramref name="cursor"/> hợp lệ.</returns>
    public static bool TryDecode(string? raw, out KeysetCursor cursor)
    {
        cursor = default;

        if (string.IsNullOrEmpty(raw) || !TryDecodeBase64Url(raw, out var bytes))
            return false;

        var parts = Encoding.UTF8.GetString(bytes).Split('|');
        if (parts.Length != 2
            || !DateTimeOffset.TryParseExact(
                parts[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt)
            || !Guid.TryParseExact(parts[1], "D", out var id))
            return false;

        // ToUniversalTime() là BẮT BUỘC, không phải dọn dẹp cho đẹp: Npgsql từ chối ghi DateTimeOffset có Offset khác 0
        // vào `timestamp with time zone` và ném "Cannot write DateTimeOffset with Offset=07:00:00…" — tức là 500 từ một
        // cursor client sửa tay. Cùng một mốc thời gian, chỉ khác cách biểu diễn, nên keyset không đổi kết quả.
        cursor = new KeysetCursor(createdAt.ToUniversalTime(), id);
        return true;
    }

    private static bool TryDecodeBase64Url(string raw, out byte[] bytes)
    {
        var padded = raw.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };

        bytes = [];
        // Length % 4 == 1 là độ dài base64 không tồn tại; TryFromBase64String bắt nốt mọi loại rác còn lại.
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
