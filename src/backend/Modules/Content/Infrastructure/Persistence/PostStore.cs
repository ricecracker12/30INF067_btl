using Microsoft.EntityFrameworkCore;
using Npgsql;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Infrastructure.Persistence;

/// <summary>
/// Hiện thực EF của <see cref="IPostStore"/>. Chỗ DUY NHẤT của module chạm <see cref="ContentDbContext"/> cho luồng ghi
/// bài — <c>Application</c> chỉ thấy interface (<c>PersistenceBoundaryTests</c> canh).
/// </summary>
public sealed class PostStore(ContentDbContext db) : IPostStore
{
    /// <summary>
    /// Tên index UNIQUE trên <c>media_attachments.storage_key</c>, do EF sinh theo quy ước và migration
    /// <c>InitialContent</c> ghi ra. Gõ tay ở đây là chấp nhận được vì nó **có test canh**: đổi tên index mà quên sửa
    /// dòng này thì <c>POST-08</c> đỏ ngay (409 rơi thành 500).
    /// </summary>
    private const string StorageKeyUniqueIndex = "IX_media_attachments_storage_key";

    /// <summary>
    /// Một <c>SaveChangesAsync</c> cho cả <c>Add(post)</c> lẫn <c>AddRange(attachments)</c> — EF gói mọi thay đổi của
    /// một lần lưu vào MỘT transaction ngầm, nên không cần <c>BeginTransactionAsync</c> tường minh ở đây.
    ///
    /// Bắt <b>đúng một</b> index. <c>uq_media_owner_position</c> hay vi phạm PK cũng là <c>UniqueViolation</c>, nhưng
    /// chúng là lỗi của CHÍNH TA khi gán <c>position</c>/<c>PostId</c> — che chúng thành 409 "ảnh đã dùng ở bài khác" là
    /// biến một lỗi lập trình thành một thông báo sai cho người dùng, và xóa luôn dấu vết trong log.
    /// </summary>
    public async Task<bool> AddWithMediaAsync(Post post, IReadOnlyList<MediaAttachment> attachments, CancellationToken ct)
    {
        db.Posts.Add(post);
        db.MediaAttachments.AddRange(attachments);

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: StorageKeyUniqueIndex,
        })
        {
            // Entity đã Add mà không lưu được thì KHÔNG được nằm lại trong tracker: DbContext là scoped theo request,
            // và một lần SaveChanges khác trong cùng request sẽ cố INSERT lại chúng.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
