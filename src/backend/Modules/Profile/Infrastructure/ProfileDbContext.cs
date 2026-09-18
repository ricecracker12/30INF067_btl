using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Profile.Domain;

namespace SocialApp.Modules.Profile.Infrastructure;

/// <summary>
/// DbContext riêng của module Profile (ADR-001 và Đ-2.1: mỗi module một context, chung một database,
/// tách nhau bằng schema). Một AppDbContext dùng chung là cửa hậu để module này query bảng của
/// module khác — ArchUnitNET không bắt được vì về kỹ thuật vẫn hợp lệ; tách schema thì ranh giới
/// hiện ra ngay trong SQL.
///
/// Bảng lịch sử migration nằm trong schema "profile" để ba context của GĐ1–GĐ2 không tranh
/// __EFMigrationsHistory (xem <see cref="ProfileDbContextOptions"/>).
///
/// KHÔNG khai extension citext như IdentityDbContext làm: Profile không có cột citext nào, và chép
/// nhầm dòng đó biến một extension của schema identity thành phụ thuộc ngầm của Profile — lúc tra
/// "ai cần citext" sẽ có hai câu trả lời.
/// </summary>
public sealed class ProfileDbContext(DbContextOptions<ProfileDbContext> options) : DbContext(options)
{
    /// <summary>Schema Postgres của module. Dùng chung cho cả bảng nghiệp vụ lẫn migration history.</summary>
    public const string Schema = "profile";

    private const string UpdatedAtProperty = nameof(UserProfile.UpdatedAt);

    public DbSet<UserProfile> Profiles => Set<UserProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProfileDbContext).Assembly);
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
    /// Gán <c>updated_at</c> cho mọi entity bị sửa có cột đó (<c>profiles</c>) — đồng hồ app, cùng nguồn
    /// với <c>created_at</c> và UUID v7 (Mục 4 "Nguồn thời gian"). Không có trigger DB nào làm việc này:
    /// <c>DEFAULT now()</c> chỉ chạy lúc INSERT.
    ///
    /// Ranh giới: <c>ExecuteUpdateAsync</c> và SQL thô đi vòng qua ChangeTracker nên KHÔNG được bảo vệ —
    /// chỗ đó phải tự <c>SetProperty(x => x.UpdatedAt, ...)</c>.
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
