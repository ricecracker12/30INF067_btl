using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Moderation.Domain;

namespace SocialApp.Modules.Moderation.Infrastructure;

/// <summary>
/// DbContext riêng của module Moderation (ADR-001, Đ-6.1: mỗi module một context, chung một database, tách nhau bằng
/// schema). Bảng lịch sử migration nằm trong schema "moderation" (xem <see cref="ModerationDbContextOptions"/>).
///
/// KHÔNG khai extension citext: Moderation không có cột citext nào.
/// KHÔNG có global query filter: báo cáo không xóa mềm, audit không xóa.
/// </summary>
public sealed class ModerationDbContext(DbContextOptions<ModerationDbContext> options) : DbContext(options)
{
    /// <summary>Schema Postgres của module. Dùng chung cho cả bảng nghiệp vụ lẫn migration history.</summary>
    public const string Schema = "moderation";

    private const string UpdatedAtProperty = nameof(Report.UpdatedAt);

    public DbSet<Report> Reports => Set<Report>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ModerationDbContext).Assembly);
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
    /// Gán <c>updated_at</c> cho mọi entity bị sửa có cột đó — chỉ <c>reports</c>. <c>audit_logs</c> không có cột này và không
    /// bao giờ bị sửa (trigger append-only, Đ-6.15), nên vòng lặp tự bỏ qua nó: KHÔNG thêm nhánh riêng cho AuditLog.
    ///
    /// Ranh giới: <c>ExecuteUpdateAsync</c> và SQL thô đi vòng qua ChangeTracker nên KHÔNG được bảo vệ — câu đóng mọi báo cáo
    /// mở của một đối tượng (D7, Đ-6.13 bước 3) phải tự gán <c>updated_at</c>.
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
