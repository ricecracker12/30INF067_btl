namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// Ai được đọc bài — khớp đúng <c>ck_posts_privacy</c> của cột <c>posts.privacy</c> (Mục 4):
/// <c>privacy IN ('public','friends','private')</c>.
///
/// Khác GĐ1 (Identity dùng lớp hằng chuỗi <c>UserStatus</c>): ở đây là <c>enum</c> theo B.3, vì BR-02
/// ở D7 phân nhánh theo từng giá trị — <c>switch</c> trên enum thì thiếu nhánh là cảnh báo của trình
/// biên dịch, còn <c>switch</c> trên chuỗi thì không.
///
/// CẠM BẪY: <c>HasConversion&lt;string&gt;()</c> của EF lưu TÊN C# (<c>"Public"</c>, chữ P hoa) trong khi
/// CHECK đòi <c>'public'</c> — mọi INSERT nổ <c>ck_posts_privacy</c> và thông báo của Postgres không nói
/// gì về hoa thường. Converter tường minh + CHECK sinh ra từ chính enum này nằm ở
/// <c>PostConfiguration</c> (A5), KHÔNG nằm ở Domain: <c>ValueConverter</c> là kiểu của EF, kéo vào
/// đây là <c>PersistenceBoundaryTests</c> đỏ.
/// </summary>
public enum PostPrivacy
{
    /// <summary>Ai đăng nhập cũng đọc được.</summary>
    Public,

    /// <summary>Chỉ bạn bè của tác giả (GĐ3 mới có quan hệ thật; GĐ2 dùng <c>AlwaysStrangers</c>, Đ-2.3).</summary>
    Friends,

    /// <summary>Chỉ chính tác giả.</summary>
    Private,
}
