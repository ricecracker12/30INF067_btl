using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SocialApp.Modules.SocialGraph.Infrastructure.Configurations;

/// <summary>
/// Một nguồn duy nhất cho ba thứ phải khớp nhau tuyệt đối: giá trị enum lưu xuống cột
/// <c>varchar</c>, danh sách trong CHECK constraint, và literal trong câu lọc của index một phần.
///
/// CẠM BẪY đây là chỗ để chặn: <c>HasConversion&lt;string&gt;()</c> có sẵn của EF lưu TÊN C#
/// (<c>"Pending"</c>, chữ P hoa) trong khi CHECK của Mục 4 đòi <c>'pending'</c> — mọi INSERT nổ
/// <c>ck_friendships_status</c> và thông báo của Postgres không nói một chữ nào về hoa thường.
///
/// Bản chép <c>internal</c> của module (L5): bản ở Content cũng <c>internal</c> và
/// <c>ModuleBoundaryTests</c> chặn import chéo; đưa lên SharedKernel sẽ kéo EF Core vào SharedKernel.
/// </summary>
internal static class LowercaseEnum
{
    /// <summary>Tên enum ở dạng chữ thường — đúng thứ DB lưu.</summary>
    internal static string Name<T>(T value) where T : struct, Enum =>
        value.ToString()!.ToLowerInvariant();

    /// <summary>
    /// Converter hai chiều. Đọc bỏ qua hoa thường để một bản dump cũ viết hoa vẫn nạp được, nhưng ghi
    /// thì LUÔN chữ thường.
    ///
    /// Tham số <c>true</c> của <c>Enum.Parse</c> cố ý viết trần: hai vế của converter là expression tree,
    /// mà expression tree không chứa được đối số có tên (<c>CS0853</c>).
    /// </summary>
    internal static ValueConverter<T, string> Converter<T>() where T : struct, Enum =>
        new(v => v.ToString()!.ToLowerInvariant(), s => Enum.Parse<T>(s, true));

    /// <summary>
    /// <c>"&lt;cột&gt; IN ('a','b')"</c> — thân của CHECK constraint, sinh từ chính enum.
    /// </summary>
    internal static string CheckSql<T>(string column) where T : struct, Enum =>
        $"{column} IN ({string.Join(",", Enum.GetNames<T>().Select(n => $"'{n.ToLowerInvariant()}'"))})";

    /// <summary>
    /// Giá trị "chưa đặt" cho <c>HasSentinel</c> — một số KHÔNG nằm trong enum, nên không entity nào mang
    /// nó và EF luôn ghi cột ra tường minh.
    ///
    /// Cần vì cột enum nào cũng có <c>DEFAULT</c> ở DB (Mục 4) trong khi giá trị hay dùng nhất lại trùng
    /// CLR default của enum (<c>Pending</c> = 0). Không khai sentinel thì EF coi "0" là "chưa đặt", bỏ cột
    /// khỏi INSERT và **cảnh báo mỗi lần dựng model**.
    /// </summary>
    internal static T NotSet<T>() where T : struct, Enum => (T)Enum.ToObject(typeof(T), -1);

    /// <summary>
    /// <c>"&lt;cột&gt; = 'giá trị'"</c> — dùng cho CHECK / filter. Chuỗi SQL thô KHÔNG đi qua
    /// <see cref="Converter{T}"/>, nên phải lấy từ đây.
    /// </summary>
    internal static string EqualsSql<T>(string column, T value) where T : struct, Enum =>
        $"{column} = '{Name(value)}'";
}
