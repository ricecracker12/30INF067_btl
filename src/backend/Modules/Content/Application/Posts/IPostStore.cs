using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// Bảng <c>content.posts</c> + <c>content.media_attachments</c> cho các luồng của khối D. Hiện thực EF nằm ở
/// <c>Infrastructure/Persistence</c> — <c>Application</c> không chạm EF (<c>PersistenceBoundaryTests</c> canh bằng máy).
///
/// Một phương thức ở D5; D6–D8 thêm <c>FindAsync</c>, <c>ListByAuthorAsync</c>, <c>MediaOfAsync</c>, <c>SaveAsync</c> khi
/// tới lượt. Không khai trước thứ chưa có người gọi — cùng nếp <c>IProfileStore</c>.
/// </summary>
public interface IPostStore
{
    /// <summary>
    /// INSERT bài và toàn bộ ảnh của nó trong MỘT transaction, để không bao giờ tồn tại một bài có
    /// <c>media_count</c> khác số dòng ảnh thật.
    /// </summary>
    /// <returns>
    /// <c>true</c> khi ghi xong; <c>false</c> khi đụng UNIQUE <c>storage_key</c> — nghĩa là một object đã được gắn vào
    /// bài khác (POST-08), và service dịch thành <b>409</b>. Mọi lỗi DB khác PHẢI ném ra ngoài thành 500: nuốt chúng
    /// thành <c>false</c> là báo cho người dùng "ảnh đã dùng" khi sự thật là hạ tầng hỏng.
    /// </returns>
    Task<bool> AddWithMediaAsync(Post post, IReadOnlyList<MediaAttachment> attachments, CancellationToken ct);
}
