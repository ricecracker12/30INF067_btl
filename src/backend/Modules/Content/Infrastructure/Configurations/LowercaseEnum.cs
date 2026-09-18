using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SocialApp.Modules.Content.Infrastructure.Configurations;

/// <summary>
/// Một nguồn duy nhất cho ba thứ phải khớp nhau tuyệt đối: giá trị enum lưu xuống cột
/// <c>varchar</c>, danh sách trong CHECK constraint, và literal trong câu lọc của index một phần.
///
/// CẠM BẪY đây là chỗ để chặn: <c>HasConversion&lt;string&gt;()</c> có sẵn của EF lưu TÊN C#
/// (<c>"Public"</c>, chữ P hoa) trong khi CHECK của Mục 4 đòi <c>'public'</c> — mọi INSERT nổ
/// <c>ck_posts_privacy</c> và thông báo của Postgres không nói một chữ nào về hoa thường.
///
/// Sinh CHECK và literal từ CHÍNH enum (đúng nếp <c>UserStatus.All</c> của GĐ1) nên thêm một giá trị
/// enum là ràng buộc DB tự rộng ra ở migration kế tiếp; gõ tay thì hai bên lệch nhau lặng lẽ.
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
    /// CLR default của enum (<c>Public</c>, <c>Published</c>, <c>Visible</c> đều là 0). Không khai sentinel
    /// thì EF coi "0" là "chưa đặt", bỏ cột khỏi INSERT và **cảnh báo mỗi lần dựng model** — tức một dòng
    /// log Warning ở mọi lần app khởi động. Kết quả ghi xuống DB thì như nhau, nhưng "như nhau" đó phụ
    /// thuộc vào việc DEFAULT của DB và default của C# không bao giờ lệch; khai sentinel thì không phải
    /// dựa vào sự trùng hợp ấy nữa, và DEFAULT ở DB trở lại đúng vai lưới cho SQL thô.
    /// </summary>
    internal static T NotSet<T>() where T : struct, Enum => (T)Enum.ToObject(typeof(T), -1);

    /// <summary>
    /// <c>"&lt;cột&gt; = 'giá trị'"</c> — dùng cho <c>HasFilter</c> của index một phần. Chuỗi SQL thô
    /// KHÔNG đi qua <see cref="Converter{T}"/>, nên phải lấy từ đây; gõ tay thì đổi cách viết của
    /// converter là index lọc sai mà không có lỗi nào báo.
    /// </summary>
    internal static string EqualsSql<T>(string column, T value) where T : struct, Enum =>
        $"{column} = '{Name(value)}'";
}
