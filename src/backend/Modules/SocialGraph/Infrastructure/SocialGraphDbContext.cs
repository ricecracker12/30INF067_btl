using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.SocialGraph.Domain;

namespace SocialApp.Modules.SocialGraph.Infrastructure;

/// <summary>
/// DbContext riêng của module SocialGraph (ADR-001 và Đ-4.1: mỗi module một context, chung một database,
/// tách nhau bằng schema). Bảng lịch sử migration nằm trong schema "socialgraph" để các context không
/// tranh <c>__EFMigrationsHistory</c> (xem <see cref="SocialGraphDbContextOptions"/>).
///
/// KHÔNG khai extension citext: SocialGraph không có cột citext nào.
/// KHÔNG có global query filter: friendships / follows không xóa mềm.
/// </summary>
public sealed class SocialGraphDbContext(DbContextOptions<SocialGraphDbContext> options) : DbContext(options)
{
    /// <summary>Schema Postgres của module. Dùng chung cho cả bảng nghiệp vụ lẫn migration history.</summary>
    public const string Schema = "socialgraph";

    private const string UpdatedAtProperty = nameof(Friendship.UpdatedAt);

    public DbSet<Friendship> Friendships => Set<Friendship>();

    public DbSet<Follow> Follows => Set<Follow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SocialGraphDbContext).Assembly);
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
    /// Gán <c>updated_at</c> cho mọi entity bị sửa có cột đó (<c>friendships</c> —
    /// <c>follows</c> không có, theo dõi không sửa). Đồng hồ app, cùng nguồn với <c>created_at</c>
    /// (Mục 4 "Nguồn thời gian"); <c>DEFAULT now()</c> của DB chỉ chạy lúc INSERT.
    ///
    /// Ranh giới: <c>ExecuteUpdateAsync</c> và SQL thô đi vòng qua ChangeTracker nên KHÔNG được bảo vệ —
    /// chỗ đó phải tự gán. Riêng module này: câu <c>UPDATE</c> chấp nhận lời mời (D3) đi bằng
    /// <c>ExecuteUpdateAsync</c>, không qua ChangeTracker, nên phải tự gán cả <c>updated_at</c> và
    /// <c>accepted_at</c> — đúng như SQL của Đ-4.14.
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
