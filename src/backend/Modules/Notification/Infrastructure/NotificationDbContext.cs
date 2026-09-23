using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Notification.Domain;

namespace SocialApp.Modules.Notification.Infrastructure;

/// <summary>
/// DbContext riêng của module Notification (ADR-001, Đ-6.1: mỗi module một context, chung một database, tách nhau bằng
/// schema). Bảng lịch sử migration nằm trong schema "notification" (xem <see cref="NotificationDbContextOptions"/>).
///
/// KHÔNG khai extension citext: Notification không có cột citext nào.
/// KHÔNG có global query filter: thông báo không xóa mềm.
///
/// KHÁC các context khác: KHÔNG override <c>SaveChanges</c> để đóng dấu <c>updated_at</c>. Ở bảng này <c>updated_at</c> là
/// "lúc sự kiện mới nhất dồn vào nhóm" và là khóa sắp danh sách (Mục 4) — không phải "lúc dòng bị sửa". Đóng dấu tự động thì
/// đánh dấu đã đọc (D11) làm thông báo cũ nhảy lên đầu. Upsert gộp (D9) tự gán cột này trong SQL.
/// </summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    /// <summary>Schema Postgres của module. Dùng chung cho cả bảng nghiệp vụ lẫn migration history.</summary>
    public const string Schema = "notification";

    public DbSet<UserNotification> Notifications => Set<UserNotification>();

    public DbSet<NotificationActor> NotificationActors => Set<NotificationActor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
