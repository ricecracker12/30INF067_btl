using SocialApp.SharedKernel.Ids;

namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// Bài đăng (ENT-02, bảng <c>content.posts</c>). Đủ 14 cột của Mục 4, không hơn — cột nào không có ở
/// Mục 4 thì sửa Mục 4 trước, trong cùng commit (AGENTS.md Mục 14).
///
/// KHÔNG có navigation property sang <c>MediaAttachment</c>: bảng ảnh là đa hình (<c>owner_type</c>)
/// nên không có FK để EF bám vào (Đ-2.12). Ảnh của một bài lấy bằng câu truy vấn tường minh theo
/// <c>(owner_type, owner_id)</c> ở D5/D6.
///
/// Cũng KHÔNG có navigation sang tác giả: <see cref="AuthorId"/> chỉ là <c>uuid</c> trần, module này
/// không nhìn thấy kiểu <c>User</c> (Đ-2.2, Đ-2.3). Tên hiển thị của tác giả đến từ contract
/// <c>IUserDirectory</c> ở SharedKernel (A6).
/// </summary>
public sealed class Post
{
    /// <summary>
    /// Khóa chính UUID v7 — KHÁC <c>UserProfile.UserId</c>: ở đây id do chính module sinh, và phải
    /// tuần tự theo thời gian vì cursor keyset của Đ-2.11 sắp <c>(created_at DESC, post_id DESC)</c>
    /// và đọc thẳng từ index. Dùng <c>Guid.NewGuid()</c> thì thứ tự PK là ngẫu nhiên và trang 2 của
    /// UC-09 có thể nhảy cóc giữa hai bài cùng mốc thời gian.
    /// </summary>
    public Guid PostId { get; init; } = Uuid7.New();

    /// <summary>
    /// Tác giả, <b>bằng</b> <c>identity.users.user_id</c> — KHÔNG FK chéo schema (Đ-2.2). Luôn lấy từ
    /// <c>User.GetUserId()</c> của token ở tầng D, không bao giờ từ body request.
    /// </summary>
    public required Guid AuthorId { get; init; }

    /// <summary>
    /// Nội dung chữ, <c>varchar(5000)</c>, <c>null</c> hoặc rỗng nếu bài chỉ có ảnh.
    ///
    /// PHẢI là <c>string?</c>: để <c>required string</c> thì bài chỉ có ảnh không tạo được và <c>BR01-06</c>
    /// đỏ. Giới hạn 5000 và luật "rỗng thì phải có ảnh" là <see cref="PostContentPolicy"/>, và DB canh
    /// lại bằng <c>ck_posts_not_empty</c>.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>Ai đọc được (BR-02). DB có CHECK <c>ck_posts_privacy</c> canh lại.</summary>
    public PostPrivacy Privacy { get; set; } = PostPrivacy.Public;

    /// <summary>Vòng đời. DB có CHECK <c>ck_posts_status</c> canh lại.</summary>
    public PostStatus Status { get; set; } = PostStatus.Published;

    /// <summary>
    /// Số ảnh đính kèm, <c>smallint</c> nên kiểu C# là <c>short</c> — để <c>int</c> thì EF sinh
    /// <c>integer</c> và migration lệch Mục 4 mà không ai thấy cho tới lúc so schema.
    ///
    /// Giữ ĐỒNG BỘ với <c>media_attachments</c> trong CÙNG một transaction: <c>ck_posts_not_empty</c> nổ
    /// ngay ở câu INSERT nếu bài chỉ có ảnh mà INSERT <c>media_count = 0</c> rồi định UPDATE sau. Service
    /// của D5 đếm ảnh TRƯỚC, INSERT post với con số đúng, rồi mới INSERT ảnh.
    /// </summary>
    public short MediaCount { get; set; }

    /// <summary>Số bình luận — GĐ3 ghi. Có sẵn từ GĐ2 để hình dạng <c>PostResponse</c> không đổi lần hai (Đ-2.12).</summary>
    public int CommentCount { get; set; }

    /// <summary>
    /// Đếm cảm xúc theo loại, cột <c>jsonb DEFAULT '{}'::jsonb</c> — GĐ3 ghi, GĐ2 KHÔNG BAO GIỜ ghi, chỉ
    /// đọc ra và trả nguyên vào DTO (Đ-2.12, Mục 8.2: <c>{}</c> chứ không <c>null</c>).
    ///
    /// Chọn <c>Dictionary&lt;string,int&gt;</c> + value converter tường minh của A5 (JsonSerializer +
    /// ValueComparer) thay vì giữ chuỗi JSON thô: D6 dựng <c>PostResponse</c> dùng thẳng, không phải parse
    /// ở tầng trên. Cái giá là A5 BẮT BUỘC khai kèm <c>ValueComparer</c> — thiếu nó EF không phát hiện
    /// thay đổi bên trong dictionary (và đó là lỗi câm, không phải lỗi biên dịch).
    ///
    /// Đường KHÔNG đi: ánh xạ POCO/Dictionary động của trình điều khiển Postgres. Bản 8 chặn trừ khi bật
    /// <c>EnableDynamicJson()</c> trên data source, và triệu chứng là exception lúc chạy CÂU TRUY VẤN ĐẦU
    /// TIÊN — tức lộ ra ở khối D chứ không ở khối A. (Tên trình điều khiển cố ý không viết ra: checklist
    /// nghiệm thu khối A grep đúng chữ đó trong hai thư mục Domain và đòi 0 kết quả.)
    /// </summary>
    public Dictionary<string, int> ReactionCounts { get; set; } = new();

    /// <summary>Lý do bị ẩn, <c>varchar(200)</c> — GĐ6 ghi (BR-07). <c>null</c> khi bài không bị ẩn.</summary>
    public string? HiddenReason { get; set; }

    /// <summary><c>null</c> = chưa sửa lần nào. D6 (PATCH) đóng dấu; <c>PostResponse.editedAt</c> đọc từ đây.</summary>
    public DateTimeOffset? EditedAt { get; set; }

    /// <summary>Thời điểm tạo, do đồng hồ app gán; <c>DEFAULT now()</c> của DB chỉ là lưới cho SQL thô.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Thời điểm sửa gần nhất — do override <c>SaveChanges</c> của <c>ContentDbContext</c> (A5) đóng dấu.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Thời điểm xóa mềm (Đ-2.10); <c>null</c> = chưa xóa. Object trên R2 do worker dọn rác xóa trễ 7 ngày
    /// (C4), không xóa đồng bộ trong request.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
