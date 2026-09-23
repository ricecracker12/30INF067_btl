using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Notification.Domain;

namespace SocialApp.Modules.Notification.Infrastructure.Configurations;

/// <summary>
/// Ánh xạ <see cref="NotificationActor"/> → <c>notification.notification_actors</c>, đúng DDL giai-doan-6.md Mục 4.
///
/// PK cặp <c>(notification_id, actor_id)</c> là thứ D9 dựa vào để đếm người khác nhau (<c>INSERT … ON CONFLICT DO NOTHING</c>
/// rồi xem có chèn được không). PK bắt đầu bằng <c>notification_id</c> nên phủ luôn FK — không cần index riêng.
///
/// FK trong schema, <c>ON DELETE CASCADE</c>: xóa một nhóm (job dọn 90 ngày của GĐ8) kéo theo người của nhóm. Dùng
/// <c>HasOne&lt;T&gt;()</c> không navigation — khuôn <c>CommentConfiguration</c>.
/// </summary>
internal sealed class NotificationActorConfiguration : IEntityTypeConfiguration<NotificationActor>
{
    public void Configure(EntityTypeBuilder<NotificationActor> builder)
    {
        builder.ToTable("notification_actors");

        builder.HasKey(x => new { x.NotificationId, x.ActorId });

        builder.Property(x => x.NotificationId).HasColumnName("notification_id");
        builder.Property(x => x.ActorId).HasColumnName("actor_id");

        builder.HasOne<UserNotification>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
    }
}
