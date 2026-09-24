using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Infrastructure;

/// <summary>
/// DbContext riêng của module Identity (ADR-001: mỗi module một context, chung một database,
/// tách nhau bằng schema). Một AppDbContext dùng chung là cửa hậu để module này query bảng của
/// module khác — ArchUnitNET không bắt được vì về kỹ thuật vẫn hợp lệ; tách schema thì ranh giới
/// hiện ra ngay trong SQL.
///
/// Bảng lịch sử migration cũng nằm trong schema "identity" để GĐ2 thêm context thứ hai không
/// tranh __EFMigrationsHistory (xem AddIdentityModule).
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    /// <summary>Schema Postgres của module. Dùng chung cho cả bảng nghiệp vụ lẫn migration history.</summary>
    public const string Schema = "identity";

    /// <summary>Sequence sinh <c>role_id</c> cho vai trò tự tạo (GĐ6, bắt đầu từ 100) — D5 gọi <c>nextval</c>.</summary>
    public const string RoleIdSequence = "roles_role_id_seq";

    private const string UpdatedAtProperty = nameof(User.UpdatedAt);

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        // Phải nằm ở ModelBuilder, không đặt được trong IEntityTypeConfiguration. EF xếp
        // CREATE EXTENSION lên đầu migration, trước bảng users dùng kiểu citext.
        modelBuilder.HasPostgresExtension("citext");
        // GĐ6 L-A4: id cho vai trò TỰ TẠO (POST /admin/roles — D5). roles.role_id vẫn ValueGeneratedNever: seeder chèn 1/2/3
        // tường minh, D5 lấy id bằng nextval. Bắt đầu từ 100 để vai trò hệ thống (kể cả nếu thêm sau này) không bao giờ đụng.
        // Khai ở model chứ không chỉ SQL tay để snapshot biết — migration sau không tưởng nó là thứ lạ.
        modelBuilder.HasSequence<short>(RoleIdSequence).StartsAt(100);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
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
    /// Gán <c>updated_at</c> cho mọi entity bị sửa có cột đó (<c>roles</c>, <c>users</c>) — đồng hồ app,
    /// cùng nguồn với <c>created_at</c> và UUID v7 (Mục 4 "Nguồn thời gian"). Không có trigger DB nào
    /// làm việc này: <c>DEFAULT now()</c> chỉ chạy lúc INSERT.
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
