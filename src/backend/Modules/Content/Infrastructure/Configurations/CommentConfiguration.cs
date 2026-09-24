using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Infrastructure.Configurations;

/// <summary>
/// Bảng <c>comments</c> (ENT-03, Mục 4 GĐ2+GĐ3). GĐ3 thêm: hai bộ đếm (<c>reply_count</c>,
/// <c>reaction_counts</c>), CHECK <c>ck_comments_root_depth</c>, và đổi bộ index cho hai trang đọc (Đ-3.6).
/// </summary>
internal sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    private static readonly ValueComparer<Dictionary<string, int>> ReactionCountsComparer = new(
        (a, b) => a != null && b != null && a.Count == b.Count && !a.Except(b).Any(),
        d => d.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key.GetHashCode(), kv.Value)),
        d => new Dictionary<string, int>(d));

    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments", t =>
        {
            t.HasCheckConstraint("ck_comments_depth", "depth BETWEEN 1 AND 3");   // BR-08, có từ GĐ2
            t.HasCheckConstraint("ck_comments_status", LowercaseEnum.CheckSql<CommentStatus>("status"));
            t.HasCheckConstraint("ck_comments_root_depth", "(parent_id IS NULL) = (depth = 1)");
            t.HasCheckConstraint("ck_comments_reply_count", "reply_count >= 0");
        });

        builder.HasKey(x => x.CommentId);
        builder.Property(x => x.CommentId).HasColumnName("comment_id").ValueGeneratedNever();

        builder.Property(x => x.PostId).HasColumnName("post_id");
        builder.Property(x => x.ParentId).HasColumnName("parent_id");
        builder.Property(x => x.AuthorId).HasColumnName("author_id");
        builder.Property(x => x.Depth).HasColumnName("depth").HasDefaultValue((short)1);
        builder.Property(x => x.Body).HasColumnName("body").HasMaxLength(1000).IsRequired();

        builder.Property(x => x.Status).HasColumnName("status")
            .HasConversion(LowercaseEnum.Converter<CommentStatus>())
            .HasMaxLength(10)
            .HasDefaultValueSql($"'{LowercaseEnum.Name(CommentStatus.Visible)}'")
            .HasSentinel(LowercaseEnum.NotSet<CommentStatus>());

        builder.Property(x => x.ReplyCount).HasColumnName("reply_count").HasDefaultValue(0);

        builder.Property(x => x.ReactionCounts).HasColumnName("reaction_counts")
            .HasColumnType("jsonb")
            .HasConversion(
                d => JsonSerializer.Serialize(d, (JsonSerializerOptions?)null),
                s => JsonSerializer.Deserialize<Dictionary<string, int>>(s, (JsonSerializerOptions?)null) ?? new())
            .Metadata.SetValueComparer(ReactionCountsComparer);

        builder.Property(x => x.ReactionCounts).HasDefaultValueSql("'{}'::jsonb");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Comment>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.PostId).HasDatabaseName("IX_comments_post_id");
        
        builder.HasIndex(x => new { x.PostId, x.CreatedAt, x.CommentId })
            .HasDatabaseName("idx_comments_post_roots")
            .HasFilter("parent_id IS NULL");

        builder.HasIndex(x => new { x.ParentId, x.CreatedAt, x.CommentId })
            .HasDatabaseName("idx_comments_parent");
    }
}