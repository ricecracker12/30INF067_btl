using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Infrastructure.Configurations;

/// <summary>
/// Bảng <c>media_attachments</c> (ENT-08, Mục 4): bốn CHECK, một UNIQUE trên <c>storage_key</c>, hai index.
///
/// KHÔNG khai <c>HasOne&lt;Post&gt;()</c> cho <c>owner_id</c>: bảng đa hình (<c>owner_type</c> nhận cả
/// <c>post</c> lẫn <c>message</c>), FK trỏ vào <c>posts</c> sẽ sai với mọi dòng của GĐ5 (Đ-2.12). Thiếu FK
/// nghĩa là DB KHÔNG dọn ảnh khi bài biến mất — và đó chính là lý do thứ hai worker dọn rác (C4) tồn tại,
/// không phải một "tối ưu hóa" ai đó có thể bỏ đi.
/// </summary>
internal sealed class MediaAttachmentConfiguration : IEntityTypeConfiguration<MediaAttachment>
{
    /// <summary>Vị trí lớn nhất: 10 ảnh thì chỉ số chạy 0..9 — buộc vào cùng hằng số với BR-01.</summary>
    private const int MaxPosition = PostContentPolicy.MaxMediaCount - 1;

    /// <summary>
    /// Allowlist loại ảnh, sinh từ <see cref="MediaAttachment.AllowedContentTypes"/> để CHECK ở DB và
    /// allowlist mà C3 ký presign không thể lệch nhau. Danh sách nằm ở Domain vì nó là luật nghiệp vụ
    /// (Đ-2.8), còn dựng câu SQL là việc của tầng này.
    /// </summary>
    private static readonly string ContentTypeCheckSql =
        $"content_type IN ({string.Join(",", MediaAttachment.AllowedContentTypes.Select(c => $"'{c}'"))})";

    public void Configure(EntityTypeBuilder<MediaAttachment> builder)
    {
        builder.ToTable("media_attachments", t =>
        {
            t.HasCheckConstraint("ck_media_owner_type", LowercaseEnum.CheckSql<MediaOwnerType>("owner_type"));
            t.HasCheckConstraint("ck_media_size", $"size_bytes > 0 AND size_bytes <= {MediaAttachment.MaxSizeBytes}");
            t.HasCheckConstraint("ck_media_content_type", ContentTypeCheckSql);
            t.HasCheckConstraint("ck_media_position", $"position BETWEEN 0 AND {MaxPosition}");
        });

        builder.HasKey(x => x.MediaId);
        builder.Property(x => x.MediaId).HasColumnName("media_id").ValueGeneratedNever();

        builder.Property(x => x.OwnerType).HasColumnName("owner_type")
            .HasConversion(LowercaseEnum.Converter<MediaOwnerType>())
            .HasMaxLength(10);

        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(40).IsRequired();
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes");
        builder.Property(x => x.Width).HasColumnName("width");
        builder.Property(x => x.Height).HasColumnName("height");
        builder.Property(x => x.Position).HasColumnName("position");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        // Chặn gắn CÙNG một object R2 vào hai bài (Mục 4, chỗ dễ sai #2). Hệ quả cho D5: commit lại cùng
        // danh sách key ném DbUpdateException, PHẢI dịch thành 409 chứ không để rơi thành 500 (POST-08).
        builder.HasIndex(x => x.StorageKey).IsUnique();

        builder.HasIndex(x => new { x.OwnerType, x.OwnerId, x.Position })
            .IsUnique()
            .HasDatabaseName("uq_media_owner_position");

        builder.HasIndex(x => new { x.OwnerType, x.OwnerId })
            .HasDatabaseName("idx_media_owner");
    }
}
