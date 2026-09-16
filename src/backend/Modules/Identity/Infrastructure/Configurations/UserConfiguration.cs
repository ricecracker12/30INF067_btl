using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Infrastructure.Configurations;

/// <summary>Bảng <c>users</c> (ENT-01, Mục 4).</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    // Dựng CHECK từ chính UserStatus.All để hằng số C# và ràng buộc DB không thể lệch nhau.
    private static readonly string StatusCheckSql =
        $"status IN ({string.Join(",", UserStatus.All.Select(s => $"'{s}'"))})";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t => t.HasCheckConstraint("ck_users_status", StatusCheckSql));

        builder.HasKey(x => x.UserId);
        // Id do Uuid7.New() sinh trong entity — không để EF chen generator thứ hai vào.
        builder.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();

        // citext: so sánh không phân biệt hoa thường ngay ở tầng DB, kể cả với unique index.
        builder.Property(x => x.Email).HasColumnName("email").HasColumnType("citext").IsRequired();
        builder.HasIndex(x => x.Email).IsUnique();

        builder.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(72).IsRequired();

        builder.Property(x => x.RoleId).HasColumnName("role_id");
        // RESTRICT phải khai tường minh: FK required thì mặc định của EF là Cascade — tức xóa vai trò
        // sẽ xóa sạch người dùng. Đây là biện pháp #1 thay cho cột is_system (Mục 3.4).
        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.EmailVerifiedAt).HasColumnName("email_verified_at");
        builder.Property(x => x.FailedLoginCount).HasColumnName("failed_login_count").HasDefaultValue((short)0);
        builder.Property(x => x.LockedUntil).HasColumnName("locked_until");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired()
            .HasDefaultValue(UserStatus.Active);

        // Xem ghi chú "Nguồn thời gian" ở RoleConfiguration.
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
    }
}
