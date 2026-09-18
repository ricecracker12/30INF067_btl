using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Profile.Domain;

namespace SocialApp.Modules.Profile.Infrastructure.Configurations;

/// <summary>Bảng <c>profiles</c> (ENT-01a, Mục 4).</summary>
internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("profiles", t =>
            t.HasCheckConstraint("ck_profiles_display_name_not_blank", "btrim(display_name) <> ''"));

        builder.HasKey(x => x.UserId);
        // KHÔNG ValueGeneratedOnAdd: user_id bằng identity.users.user_id, đến từ token ở tầng D —
        // không do DB hay EF sinh. Cũng không có FK sang identity.users: Đ-2.2 cấm FK chéo schema.
        builder.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();

        // 50 lấy từ hằng số của entity để cột DB và validator của D2 không thể lệch nhau (Đ-2.4).
        builder.Property(x => x.DisplayName).HasColumnName("display_name")
            .HasMaxLength(UserProfile.DisplayNameMaxLength).IsRequired();

        builder.Property(x => x.Bio).HasColumnName("bio").HasMaxLength(500);
        builder.Property(x => x.AvatarKey).HasColumnName("avatar_key").HasMaxLength(200);

        // Nguồn thời gian: giá trị do đồng hồ app gán trong entity; DEFAULT now() chỉ là lưới cho SQL
        // thô. updated_at còn được ProfileDbContext.StampUpdatedAt đóng dấu ở mỗi lần sửa.
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
    }
}
