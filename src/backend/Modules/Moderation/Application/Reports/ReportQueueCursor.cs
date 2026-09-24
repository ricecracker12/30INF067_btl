using System.Globalization;
using System.Text;

namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>
/// Con trỏ keyset của <c>GET /reports</c> (GĐ6 D7b): <c>(firstReportedAt, targetId)</c> của dòng CUỐI trang trước. Sắp
/// <c>first_reported_at ASC</c>, hòa thì <c>target_id ASC</c> — khóa phụ bắt buộc: hai đối tượng bị báo cùng một micro giây mà thiếu
/// nó thì trùng/sót giữa hai trang. Không cần <c>target_type</c>: UUID v7 không trùng giữa bảng bài và bảng người dùng.
///
/// Chép khuôn <c>AdminUserCursor</c> (Identity) chứ không dùng chung — <c>ModuleBoundaryTests</c> chặn import chéo module. Opaque với
/// client: base64url của <c>"{firstReportedAt:O}|{targetId:D}"</c>. Không ký: hai giá trị đã nằm trên <c>ReportQueueItem</c>.
/// </summary>
public readonly record struct ReportQueueCursor(DateTimeOffset FirstReportedAt, Guid TargetId)
{
    /// <summary>Base64url thủ công — .NET 8 chưa có <c>Base64Url</c>, và <c>+</c>, <c>/</c> hỏng sau một vòng query string.</summary>
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{FirstReportedAt:O}|{TargetId:D}"))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <summary>Không ném với bất kỳ đầu vào nào — cursor sửa tay phải ra 400 <c>errors.cursor</c>, không phải 500.</summary>
    public static bool TryDecode(string? raw, out ReportQueueCursor cursor)
    {
        cursor = default;

        if (string.IsNullOrEmpty(raw) || !TryDecodeBase64Url(raw, out var bytes))
            return false;

        var parts = Encoding.UTF8.GetString(bytes).Split('|');
        if (parts.Length != 2
            || !DateTimeOffset.TryParseExact(
                parts[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var firstReportedAt)
            || !Guid.TryParseExact(parts[1], "D", out var targetId))
            return false;

        // ToUniversalTime() bắt buộc: Npgsql từ chối DateTimeOffset có Offset khác 0 — 500 từ cursor sửa tay.
        cursor = new ReportQueueCursor(firstReportedAt.ToUniversalTime(), targetId);
        return true;
    }

    private static bool TryDecodeBase64Url(string raw, out byte[] bytes)
    {
        var padded = raw.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };

        bytes = [];
        if (padded.Length % 4 != 0)
            return false;

        var buffer = new byte[padded.Length * 3 / 4];
        if (!Convert.TryFromBase64String(padded, buffer, out var written))
            return false;

        bytes = buffer[..written];
        return true;
    }
}
