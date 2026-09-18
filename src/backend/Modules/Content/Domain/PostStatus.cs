namespace SocialApp.Modules.Content.Domain;

/// <summary>
/// Vòng đời của bài — khớp đúng <c>ck_posts_status</c> (Mục 4):
/// <c>status IN ('published','hidden','deleted')</c>. Cùng luật converter với <see cref="PostPrivacy"/>.
/// </summary>
public enum PostStatus
{
    /// <summary>Bình thường, hiện trong mọi luồng đọc.</summary>
    Published,

    /// <summary>Bị kiểm duyệt ẩn (GĐ6 ghi, kèm <c>hidden_reason</c> — BR-07).</summary>
    Hidden,

    /// <summary>
    /// Đã xóa mềm (Đ-2.10). Global query filter của <c>ContentDbContext</c> (A5) loại giá trị này khỏi
    /// mọi câu truy vấn; chỗ duy nhất được <c>IgnoreQueryFilters</c> là repository của worker dọn rác (C4).
    /// </summary>
    Deleted,
}
