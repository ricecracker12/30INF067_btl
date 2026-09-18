using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Infrastructure.Configurations;

/// <summary>
/// Bảng <c>comments</c> (ENT-03, Mục 4) — KHUNG của Đ-2.12: ràng buộc đầy đủ, GĐ2 không có endpoint nào.
///
/// Hai khóa ngoại được GIỮ vì cả hai nằm TRONG schema <c>content</c> — Đ-2.2 chỉ cấm FK đi qua ranh giới
/// schema. Dùng <c>HasOne&lt;T&gt;()</c> KHÔNG có navigation property: khung không có hành vi, và mỗi
/// navigation là một đường để code sau này vô tình <c>.Include()</c> cả bảng.
/// </summary>
internal sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments", t =>
        {
            t.HasCheckConstraint("ck_comments_depth", "depth BETWEEN 1 AND 3");   // BR-08
            t.HasCheckConstraint("ck_comments_status", LowercaseEnum.CheckSql<CommentStatus>("status"));
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

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        // Tên index EF sinh mặc định là IX_comments_post_id / IX_comments_parent_id — ĐÚNG Y tên trong
        // DDL của Mục 4, nên cố ý không HasDatabaseName để khỏi lệch.
        builder.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Comment>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Cascade);
    }
}
