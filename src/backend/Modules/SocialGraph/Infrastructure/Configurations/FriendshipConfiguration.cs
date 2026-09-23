using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.SocialGraph.Domain;

namespace SocialApp.Modules.SocialGraph.Infrastructure.Configurations;

/// <summary>
/// Ánh xạ <see cref="Friendship"/> → <c>socialgraph.friendships</c>. PK cặp + bốn CHECK giao BR-03
/// cho Postgres giữ — kể cả SQL thô của D3 cũng không đi vòng được.
/// </summary>
internal sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    private const string StatusColumn = "status";

    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        var accepted = LowercaseEnum.EqualsSql(StatusColumn, FriendshipStatus.Accepted);

        builder.ToTable("friendships", t =>
        {
            t.HasCheckConstraint("ck_friendships_order", "user_min_id < user_max_id");
            t.HasCheckConstraint("ck_friendships_requester", "requester_id IN (user_min_id, user_max_id)");
            t.HasCheckConstraint("ck_friendships_status", LowercaseEnum.CheckSql<FriendshipStatus>(StatusColumn));
            t.HasCheckConstraint("ck_friendships_accepted", $"({accepted}) = (accepted_at IS NOT NULL)");
        });

        builder.HasKey(x => new { x.UserMinId, x.UserMaxId });

        builder.Property(x => x.UserMinId).HasColumnName("user_min_id").ValueGeneratedNever();
        builder.Property(x => x.UserMaxId).HasColumnName("user_max_id").ValueGeneratedNever();
        builder.Property(x => x.RequesterId).HasColumnName("requester_id");

        builder.Property(x => x.Status).HasColumnName(StatusColumn)
            .HasMaxLength(10)
            .HasConversion(LowercaseEnum.Converter<FriendshipStatus>())
            .HasDefaultValueSql($"'{LowercaseEnum.Name(FriendshipStatus.Pending)}'")
            .HasSentinel(LowercaseEnum.NotSet<FriendshipStatus>());

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(x => x.AcceptedAt).HasColumnName("accepted_at");

        // PK phục vụ tra theo user_min_id. "Bạn của tôi" khi tôi là user_max_id cần index riêng — thiếu nó thì
        // FeedSourceReader (A5) quét tuần tự nửa bảng ở mỗi request feed trượt cache.
        builder.HasIndex(x => x.UserMaxId).HasDatabaseName("idx_friendships_user_max");
    }
}
