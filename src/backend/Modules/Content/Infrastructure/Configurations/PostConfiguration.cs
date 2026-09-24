using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Infrastructure.Configurations;

/// <summary>Bảng <c>posts</c> (ENT-02, Mục 4): bốn CHECK, hai index một phần (UC-09 + feed gợi ý Đ-4.6), và global query filter của Đ-2.10.</summary>
internal sealed class PostConfiguration : IEntityTypeConfiguration<Post>
{
    private const string StatusColumn = "status";
    private const string PrivacyColumn = "privacy";

    /// <summary>
    /// jsonb ⇄ Dictionary. <c>ValueComparer</c> là BẮT BUỘC, không phải trang trí: thiếu nó EF so sánh
    /// dictionary bằng tham chiếu, nên sửa bên trong (GĐ3 tăng một loại cảm xúc) KHÔNG được phát hiện và
    /// <c>SaveChanges</c> im lặng không ghi gì. Lỗi câm, không phải lỗi biên dịch.
    /// </summary>
    private static readonly ValueComparer<Dictionary<string, int>> ReactionCountsComparer = new(
        (a, b) => a != null && b != null && a.Count == b.Count && !a.Except(b).Any(),
        d => d.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key.GetHashCode(), kv.Value)),
        d => new Dictionary<string, int>(d));

    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.ToTable("posts", t =>
        {
            t.HasCheckConstraint("ck_posts_privacy", LowercaseEnum.CheckSql<PostPrivacy>(PrivacyColumn));
            t.HasCheckConstraint("ck_posts_status", LowercaseEnum.CheckSql<PostStatus>(StatusColumn));
            t.HasCheckConstraint("ck_posts_media_count", $"media_count BETWEEN 0 AND {PostContentPolicy.MaxMediaCount}");
            // BR-01 ở tầng DB. HỆ QUẢ: INSERT phải mang media_count ĐÚNG ngay từ đầu — bài chỉ có ảnh mà
            // INSERT 0 rồi định UPDATE sau thì CHECK nổ ngay ở câu INSERT (Mục 4, chỗ dễ sai #1).
            t.HasCheckConstraint("ck_posts_not_empty", "media_count > 0 OR btrim(coalesce(body,'')) <> ''");
            t.HasCheckConstraint("ck_posts_comment_count", "comment_count >= 0");   // Mới GĐ3, Mục 4
        });

        builder.HasKey(x => x.PostId);
        // KHÔNG ValueGeneratedOnAdd: id do Uuid7.New() của entity sinh, không do DB.
        builder.Property(x => x.PostId).HasColumnName("post_id").ValueGeneratedNever();

        builder.Property(x => x.AuthorId).HasColumnName("author_id");
        builder.Property(x => x.Body).HasColumnName("body").HasMaxLength(PostContentPolicy.MaxBodyLength);

        // HasSentinel: xem ghi chú ở LowercaseEnum.NotSet. Thiếu nó thì EF cảnh báo mỗi lần dựng model
        // (log Warning ở mọi lần app khởi động) vì Public là CLR default của enum.
        builder.Property(x => x.Privacy).HasColumnName(PrivacyColumn)
            .HasConversion(LowercaseEnum.Converter<PostPrivacy>())
            .HasMaxLength(10)
            .HasDefaultValueSql($"'{LowercaseEnum.Name(PostPrivacy.Public)}'")
            .HasSentinel(LowercaseEnum.NotSet<PostPrivacy>());

        builder.Property(x => x.Status).HasColumnName(StatusColumn)
            .HasConversion(LowercaseEnum.Converter<PostStatus>())
            .HasMaxLength(10)
            .HasDefaultValueSql($"'{LowercaseEnum.Name(PostStatus.Published)}'")
            .HasSentinel(LowercaseEnum.NotSet<PostStatus>());

        builder.Property(x => x.MediaCount).HasColumnName("media_count").HasDefaultValue((short)0);
        builder.Property(x => x.CommentCount).HasColumnName("comment_count").HasDefaultValue(0);

        builder.Property(x => x.ReactionCounts).HasColumnName("reaction_counts")
            .HasColumnType("jsonb")
            .HasConversion(
                d => JsonSerializer.Serialize(d, (JsonSerializerOptions?)null),
                s => JsonSerializer.Deserialize<Dictionary<string, int>>(s, (JsonSerializerOptions?)null) ?? new())
            .Metadata.SetValueComparer(ReactionCountsComparer);

        // '::jsonb' BẮT BUỘC (Mục 4, chỗ dễ sai #3): thiếu cast thì default mang kiểu text và migration
        // đỏ trên Postgres 16.
        builder.Property(x => x.ReactionCounts).HasDefaultValueSql("'{}'::jsonb");

        builder.Property(x => x.HiddenReason).HasColumnName("hidden_reason").HasMaxLength(200);
        builder.Property(x => x.EditedAt).HasColumnName("edited_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        // Index của UC-09 (trang cá nhân), GĐ4 dùng lại cho feed. IsDescending khớp đúng thứ tự sắp của
        // cursor keyset (Đ-2.11) nên trang 2 đọc thẳng từ index, không sort lại. Bỏ nó thì PAGE-01 vẫn
        // xanh và endpoint vẫn đúng — chỉ chậm, và chỉ lộ ra ở k6 của GĐ4.
        builder.HasIndex(x => new { x.AuthorId, x.CreatedAt, x.PostId })
            .HasDatabaseName("idx_posts_author_created")
            .IsDescending(false, true, true)
            // Chuỗi SQL thô này KHÔNG đi qua converter — lấy từ LowercaseEnum để không lệch (cạm bẫy 2 của A5).
            .HasFilter(LowercaseEnum.EqualsSql(StatusColumn, PostStatus.Published));

        // Feed gợi ý (Đ-4.6, GĐ4): bài công khai mới nhất TOÀN HỆ THỐNG. idx_posts_author_created không phục vụ được
        // truy vấn không có author_id. Hai literal lấy từ LowercaseEnum — chuỗi HasFilter không đi qua converter.
        builder.HasIndex(x => new { x.CreatedAt, x.PostId })
            .HasDatabaseName("idx_posts_public_recent")
            .IsDescending(true, true)
            .HasFilter(
                $"{LowercaseEnum.EqualsSql(StatusColumn, PostStatus.Published)} AND " +
                $"{LowercaseEnum.EqualsSql(PrivacyColumn, PostPrivacy.Public)}");

        // Xóa bài là xóa mềm (Đ-2.10): bài đã xóa biến khỏi MỌI truy vấn đọc qua DbSet, không phải nhớ
        // thêm 'where' ở từng chỗ.
        //
        // NGOẠI LỆ DUY NHẤT trong cả repo được gọi IgnoreQueryFilters(): repository của worker dọn rác
        // (C4) — nó phải thấy chính những bài đã xóa để xóa object trên R2 sau 7 ngày. Chỗ thứ hai xuất
        // hiện nghĩa là ai đó đang đi vòng qua Đ-2.10.
        builder.HasQueryFilter(p => p.Status != PostStatus.Deleted);
    }
}
