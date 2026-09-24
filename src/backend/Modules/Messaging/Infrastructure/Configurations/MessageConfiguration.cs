using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Messaging.Domain;

namespace SocialApp.Modules.Messaging.Infrastructure.Configurations;

/// <summary>
/// Ánh xạ <see cref="Message"/> → <c>messaging.messages</c> (giai-doan-5.md Mục 4). Hai UNIQUE là lưới cuối của Đ-5.4/Đ-5.5:
/// <c>seq</c> không trùng trong một hội thoại, và <c>client_msg_id</c> không tạo tin thứ hai (US-015 AC-03).
/// </summary>
internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages", t =>
        {
            t.HasCheckConstraint("ck_messages_seq_positive", "seq > 0");
            t.HasCheckConstraint("ck_messages_content", "char_length(content) BETWEEN 1 AND 2000 AND btrim(content) <> ''");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();   // UUID v7 app sinh TRƯỚC transaction (Đ-5.4)

        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.SenderId).HasColumnName("sender_id");            // KHÔNG FK (Đ-5.1)
        builder.Property(x => x.Seq).HasColumnName("seq");
        builder.Property(x => x.Content).HasColumnName("content").HasMaxLength(Domain.MessageContentPolicy.MaxLength);
        builder.Property(x => x.ClientMsgId).HasColumnName("client_msg_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        // FK trong CÙNG schema giữ nguyên (Đ-5.1). Không navigation: Message không cần biết Conversation ở tầng object.
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_messages_conversation");

        // Cũng là index của lịch sử: B-tree đọc ngược được nên KHÔNG tạo thêm idx(conversation_id, seq DESC) trùng cột (Mục 4,
        // chỗ dễ sai số 3). EXPLAIN phải thấy Index Scan Backward.
        builder.HasIndex(x => new { x.ConversationId, x.Seq }).IsUnique().HasDatabaseName("uq_messages_conv_seq");
        builder.HasIndex(x => new { x.ConversationId, x.ClientMsgId }).IsUnique().HasDatabaseName("uq_messages_conv_client_id");
    }
}
