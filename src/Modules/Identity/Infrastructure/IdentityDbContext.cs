using Microsoft.EntityFrameworkCore;

namespace SocialApp.Modules.Identity.Infrastructure;

/// <summary>
/// DbContext riêng của module Identity (ADR-001: mỗi module một context, chung một database,
/// tách nhau bằng schema). Một AppDbContext dùng chung là cửa hậu để module này query bảng của
/// module khác — ArchUnitNET không bắt được vì về kỹ thuật vẫn hợp lệ; tách schema thì ranh giới
/// hiện ra ngay trong SQL.
///
/// Bảng lịch sử migration cũng nằm trong schema "identity" để GĐ2 thêm context thứ hai không
/// tranh __EFMigrationsHistory (xem AddIdentityModule).
///
/// GĐ1 khối A bổ sung entity: users, roles, permissions, role_permissions, refresh_tokens,
/// email_verification_tokens.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    /// <summary>Schema Postgres của module. Dùng chung cho cả bảng nghiệp vụ lẫn migration history.</summary>
    public const string Schema = "identity";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
