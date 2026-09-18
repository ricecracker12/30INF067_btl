using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Infrastructure.Configurations;

/// <summary>
/// Bảng <c>reactions</c> (ENT-05, Mục 4) — KHUNG của Đ-2.12.
///
/// Khóa chính BA CỘT <c>(user_id, target_type, target_id)</c> <b>chính là BR-05</b>: một người, một đối
/// tượng, một cảm xúc. Đổi sang khóa đơn là gỡ BR-05 khỏi tầng DB, và GĐ2 không có test nào bắt được vì
/// GĐ2 chưa ghi bảng này.
///
/// KHÔNG có FK tới <c>posts</c>/<c>comments</c>: <c>target_id</c> đa hình, cùng lý do với
/// <c>media_attachments</c>.
/// </summary>
internal sealed class ReactionConfiguration : IEntityTypeConfiguration<Reaction>
{
    public void Configure(EntityTypeBuilder<Reaction> builder)
    {
        builder.ToTable("reactions", t =>
        {
            t.HasCheckConstraint("ck_reactions_target", LowercaseEnum.CheckSql<ReactionTargetType>("target_type"));
            t.HasCheckConstraint("ck_reactions_type", LowercaseEnum.CheckSql<ReactionType>("type"));
        });

        builder.HasKey(x => new { x.UserId, x.TargetType, x.TargetId });

        builder.Property(x => x.UserId).HasColumnName("user_id");

        builder.Property(x => x.TargetType).HasColumnName("target_type")
            .HasConversion(LowercaseEnum.Converter<ReactionTargetType>())
            .HasMaxLength(10);

        builder.Property(x => x.TargetId).HasColumnName("target_id");

        builder.Property(x => x.Type).HasColumnName("type")
            .HasConversion(LowercaseEnum.Converter<ReactionType>())
            .HasMaxLength(10);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(x => new { x.TargetType, x.TargetId }).HasDatabaseName("idx_reactions_target");
    }
}
