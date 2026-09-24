using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Messaging.Domain;

namespace SocialApp.Modules.Messaging.Infrastructure;

/// <summary>
/// DbContext riêng của module Messaging (ADR-001, Đ-5.1: mỗi module một context, chung một database, tách nhau bằng schema).
/// Bảng lịch sử migration nằm trong schema "messaging" để các context không tranh <c>__EFMigrationsHistory</c> (xem
/// <see cref="MessagingDbContextOptions"/>).
///
/// KHÔNG có global query filter: hội thoại và tin không xóa mềm trong MVP (xóa/ẩn danh khi xóa tài khoản là nợ GĐ8).
/// </summary>
public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options) : DbContext(options)
{
    /// <summary>Schema Postgres của module. Dùng chung cho cả bảng nghiệp vụ lẫn migration history.</summary>
    public const string Schema = "messaging";

    private const string UpdatedAtProperty = nameof(Conversation.UpdatedAt);

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MessagingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

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
    /// Gán <c>updated_at</c> cho entity bị sửa CÓ cột đó — chỉ <c>conversations</c>. <c>messages</c> không có cột
    /// <c>updated_at</c> (Mục 4, chỗ dễ sai số 1): <c>FindProperty</c> trả null nên bỏ qua, không đòi cột không tồn tại.
    ///
    /// Ranh giới: SQL thô (câu gửi tin Đ-5.4, câu cập nhật mốc) đi vòng ChangeTracker nên KHÔNG được bảo vệ ở đây — câu SQL
    /// tự gán <c>updated_at</c>.
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
