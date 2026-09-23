using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Notification.Domain;

namespace SocialApp.Modules.Notification.Infrastructure.Configurations;

/// <summary>
/// Ánh xạ <see cref="UserNotification"/> → <c>notification.notifications</c>, đúng DDL giai-doan-6.md Mục 4. Không FK nào ra
/// ngoài schema (Đ-2.2).
///
/// CHECK loại dựng từ <see cref="NotificationTypes.All"/> (khuôn <c>UserConfiguration.StatusCheckSql</c>): thêm loại mới là sửa
/// hằng + migration mới, hằng và ràng buộc không lệch nhau được.
/// </summary>
internal sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("notifications", t =>
        {
            t.HasCheckConstraint("ck_notifications_type", InSql("type", NotificationTypes.All));
            t.HasCheckConstraint("ck_notifications_actor_count", "actor_count >= 1");
        });

        builder.HasKey(x => x.Id);
        // UUID v7 do app sinh — không để EF chen generator thứ hai vào.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.RecipientId).HasColumnName("recipient_id");
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(NotificationTypes.MaxLength).IsRequired();
        builder.Property(x => x.GroupKey).HasColumnName("group_key").HasMaxLength(GroupKey.MaxLength).IsRequired();
        builder.Property(x => x.TargetType).HasColumnName("target_type").HasMaxLength(NotificationTargetTypes.MaxLength).IsRequired();
        builder.Property(x => x.TargetId).HasColumnName("target_id");
        builder.Property(x => x.PostId).HasColumnName("post_id");
        builder.Property(x => x.LastActorId).HasColumnName("last_actor_id");
        builder.Property(x => x.ActorCount).HasColumnName("actor_count").HasDefaultValue(1);
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(20);
        builder.Property(x => x.IsRead).HasColumnName("is_read").HasDefaultValue(false);

        // Nguồn thời gian là đồng hồ app; DEFAULT now() chỉ là lưới cho INSERT bằng SQL thô (D9 đi đường đó).
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        // Đ-6.16: gộp. UNIQUE CONSTRAINT (không chỉ unique index) đúng tên DDL Mục 4 — D9 viết
        //   ON CONFLICT (recipient_id, group_key) DO UPDATE …
        // Câu đó chạy được với cả hai loại, nhưng chốt MỘT hình dạng và khóa nó bằng schema test để D9 không phải đoán
        // (NotificationDbContextSchemaTests đọc pg_constraint contype = 'u').
        builder.HasAlternateKey(x => new { x.RecipientId, x.GroupKey })
            .HasName("uq_notifications_group");

        // Danh sách: updated_at DESC, id DESC — nhóm vừa có sự kiện mới nhảy lên đầu (NotificationPage, keyset).
        builder.HasIndex(x => new { x.RecipientId, x.UpdatedAt, x.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("idx_notifications_recent");

        // Badge số chưa đọc (PTTK 5.6) — index một phần, chỉ dòng chưa đọc.
        builder.HasIndex(x => x.RecipientId)
            .HasFilter("is_read = false")
            .HasDatabaseName("idx_notifications_unread");
    }

    private static string InSql(string column, IEnumerable<string> values) =>
        $"{column} IN ({string.Join(",", values.Select(v => $"'{v}'"))})";
}
