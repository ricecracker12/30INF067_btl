using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Infrastructure;

/// <summary>
/// DbContext riêng của module Content (ADR-001 và Đ-2.1: mỗi module một context, chung một database,
/// tách nhau bằng schema). Bảng lịch sử migration nằm trong schema "content" để ba context của
/// GĐ1–GĐ2 không tranh <c>__EFMigrationsHistory</c> (xem <see cref="ContentDbContextOptions"/>).
///
/// KHÔNG khai extension citext: Content không có cột citext nào (chỉ IdentityDbContext cần, cho
/// <c>users.email</c>).
///
/// Bốn <see cref="DbSet{TEntity}"/>, trong đó <see cref="Comments"/> và <see cref="Reactions"/> là
/// KHUNG của Đ-2.12 — có bảng, có ràng buộc, nhưng GĐ2 không có endpoint nào chạm tới.
/// </summary>
public sealed class ContentDbContext(DbContextOptions<ContentDbContext> options) : DbContext(options)
{
    /// <summary>Schema Postgres của module. Dùng chung cho cả bảng nghiệp vụ lẫn migration history.</summary>
    public const string Schema = "content";

    private const string UpdatedAtProperty = nameof(Post.UpdatedAt);

    /// <summary>
    /// Bài đăng. CHÚ Ý: có global query filter loại <see cref="PostStatus.Deleted"/> (Đ-2.10, xem
    /// <c>PostConfiguration</c>) — mọi truy vấn qua DbSet này đều KHÔNG thấy bài đã xóa mềm.
    /// </summary>
    public DbSet<Post> Posts => Set<Post>();

    public DbSet<MediaAttachment> MediaAttachments => Set<MediaAttachment>();

    /// <summary>Khung Đ-2.12 — GĐ2 không ghi.</summary>
    public DbSet<Comment> Comments => Set<Comment>();

    /// <summary>Khung Đ-2.12 — GĐ2 không ghi.</summary>
    public DbSet<Reaction> Reactions => Set<Reaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContentDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    // Hai overload nhận acceptAllChangesOnSuccess là đích cuối của cả bốn đường SaveChanges /
    // SaveChangesAsync, nên chỉ cần chặn ở đây.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampUpdatedAt();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampUpdatedAt();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Gán <c>updated_at</c> cho mọi entity bị sửa có cột đó (<c>posts</c>, <c>comments</c>,
    /// <c>reactions</c> — <c>media_attachments</c> không có, ảnh đã gắn thì không sửa). Đồng hồ app,
    /// cùng nguồn với <c>created_at</c> và UUID v7 (Mục 4 "Nguồn thời gian"); <c>DEFAULT now()</c> của
    /// DB chỉ chạy lúc INSERT.
    ///
    /// Ranh giới: <c>ExecuteUpdateAsync</c> và SQL thô đi vòng qua ChangeTracker nên KHÔNG được bảo vệ —
    /// chỗ đó phải tự <c>SetProperty(x => x.UpdatedAt, ...)</c>. Xóa mềm của D6 đi qua ChangeTracker nên
    /// vẫn được đóng dấu.
    /// </summary>
    private void StampUpdatedAt()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Modified && entry.Metadata.FindProperty(UpdatedAtProperty) is not null)
                entry.Property(UpdatedAtProperty).CurrentValue = now;
        }
    }
}
