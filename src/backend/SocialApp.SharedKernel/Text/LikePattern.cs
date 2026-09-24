namespace SocialApp.SharedKernel.Text;

/// <summary>
/// Dựng mẫu <c>LIKE</c> từ chuỗi người dùng gõ — MỘT chỗ cho mọi module (GĐ6: <c>GET /admin/users?q=</c> của Identity ở D2,
/// <c>GET /search</c> của Profile ở D12). Hai bản chép tay lệch nhau là cách một màn hiểu <c>_</c> theo nghĩa đen còn màn kia
/// hiểu là "một ký tự bất kỳ" (cạm bẫy 3 của D2, hướng dẫn khối D GĐ6).
///
/// Ký tự thoát là <see cref="EscapeCharacter"/> (<c>\</c>). Truyền nó TƯỜNG MINH cho câu SQL
/// (<c>EF.Functions.Like(col, pattern, LikePattern.EscapeCharacter)</c>): mặc định của Postgres cũng là <c>\</c>, nhưng dựa vào
/// mặc định là dựa vào một thiết lập server không ai nhìn thấy.
/// </summary>
public static class LikePattern
{
    /// <summary>Ký tự thoát của mọi mẫu dựng ở đây.</summary>
    public const string EscapeCharacter = "\\";

    /// <summary>
    /// Thoát <c>\</c>, <c>%</c>, <c>_</c> để <paramref name="input"/> khớp theo nghĩa đen. <c>\</c> thoát TRƯỚC: thoát sau thì
    /// dấu <c>\</c> vừa chèn cho <c>%</c> bị nhân đôi và <c>%</c> lại thành ký tự đại diện.
    /// </summary>
    public static string Escape(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input
            .Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter, StringComparison.Ordinal)
            .Replace("%", EscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", EscapeCharacter + "_", StringComparison.Ordinal);
    }

    /// <summary>Mẫu "bắt đầu bằng <paramref name="prefix"/>" — <paramref name="prefix"/> hiểu theo nghĩa đen.</summary>
    public static string StartsWith(string prefix) => Escape(prefix) + "%";
}
