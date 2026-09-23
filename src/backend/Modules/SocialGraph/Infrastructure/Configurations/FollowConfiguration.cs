using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.SocialGraph.Domain;

namespace SocialApp.Modules.SocialGraph.Infrastructure.Configurations;

/// <summary>
/// Ánh xạ <see cref="Follow"/> → <c>socialgraph.follows</c>. PK cặp có hướng; CHECK không tự theo dõi.
/// Không index theo <c>followee_id</c>: "ai theo dõi tôi" chưa có người đọc ở GĐ4 (Mục 4).
/// </summary>
internal sealed class FollowConfiguration : IEntityTypeConfiguration<Follow>
{
    public void Configure(EntityTypeBuilder<Follow> builder)
    {
        builder.ToTable("follows", t =>
            t.HasCheckConstraint("ck_follows_not_self", "follower_id <> followee_id"));

        builder.HasKey(x => new { x.FollowerId, x.FolloweeId });

        builder.Property(x => x.FollowerId).HasColumnName("follower_id").ValueGeneratedNever();
        builder.Property(x => x.FolloweeId).HasColumnName("followee_id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
    }
}
