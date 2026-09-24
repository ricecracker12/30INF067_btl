using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Messaging.Domain;

namespace SocialApp.Modules.Messaging.Infrastructure.Configurations;

/// <summary>
/// Ánh xạ <see cref="Conversation"/> → <c>messaging.conversations</c> (giai-doan-5.md Mục 4). BR-06 (một hội thoại mỗi cặp,
/// không tự nhắn cho mình) và bất biến mốc Đ-5.6 do Postgres giữ — kể cả SQL thô của D5/D6 cũng không đi vòng được.
/// </summary>
internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations", t =>
        {
            // Thứ tự uuid của Postgres (Đ-5.2) — ConversationPair.Of chuẩn hóa đúng thứ tự này.
            t.HasCheckConstraint("ck_conversations_order", "user_a_id < user_b_id");
            // Lưới cho Đ-5.6: câu UPDATE mốc quên kẹp ở seq_counter nổ ở DB thay vì ghi "đã xem tin tương lai".
            t.HasCheckConstraint("ck_conversations_marks",
                "user_a_seen_seq <= user_a_delivered_seq AND user_a_delivered_seq <= seq_counter AND "
              + "user_b_seen_seq <= user_b_delivered_seq AND user_b_delivered_seq <= seq_counter");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        // uuid trần, KHÔNG FK sang identity.users (Đ-5.1, cùng lý do Đ-2.2).
        builder.Property(x => x.UserAId).HasColumnName("user_a_id");
        builder.Property(x => x.UserBId).HasColumnName("user_b_id");

        builder.Property(x => x.SeqCounter).HasColumnName("seq_counter").HasDefaultValue(0L);

        // KHÔNG FK tới messages dù cùng schema (Đ-5.1): hai bảng trỏ vòng vào nhau, FK thường làm câu UPDATE ở bước 4 của Đ-5.4
        // đỏ (tin chưa INSERT lúc cập nhật con trỏ), FK DEFERRABLE thì EF không khai báo được. Con trỏ ghi trong CÙNG transaction
        // với tin nên không lệch được.
        builder.Property(x => x.LastMessageId).HasColumnName("last_message_id");
        builder.Property(x => x.LastMessageAt).HasColumnName("last_message_at");

        builder.Property(x => x.UserADeliveredSeq).HasColumnName("user_a_delivered_seq").HasDefaultValue(0L);
        builder.Property(x => x.UserASeenSeq).HasColumnName("user_a_seen_seq").HasDefaultValue(0L);
        builder.Property(x => x.UserBDeliveredSeq).HasColumnName("user_b_delivered_seq").HasDefaultValue(0L);
        builder.Property(x => x.UserBSeenSeq).HasColumnName("user_b_seen_seq").HasDefaultValue(0L);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        // Một hội thoại mỗi cặp — đích của INSERT … ON CONFLICT (user_a_id, user_b_id) DO NOTHING (Đ-5.2).
        builder.HasIndex(x => new { x.UserAId, x.UserBId }).IsUnique().HasDatabaseName("uq_conversations_pair");

        // Danh sách hội thoại của một người: hai phía, keyset (last_message_at DESC, id DESC). Chỉ hội thoại đã có tin — hội
        // thoại rỗng không hiện trong danh sách (LIST-01). Truy vấn danh sách là UNION ALL hai nhánh, mỗi nhánh dùng index của
        // nó; WHERE a = @me OR b = @me thường ra Seq Scan (B.4 A5).
        builder.HasIndex(x => new { x.UserAId, x.LastMessageAt, x.Id })
            .IsDescending(false, true, true)
            .HasFilter("last_message_at IS NOT NULL")
            .HasDatabaseName("idx_conversations_a_recent");
        builder.HasIndex(x => new { x.UserBId, x.LastMessageAt, x.Id })
            .IsDescending(false, true, true)
            .HasFilter("last_message_at IS NOT NULL")
            .HasDatabaseName("idx_conversations_b_recent");
    }
}
