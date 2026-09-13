using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Infrastructure.Configurations;

/// <summary>Bảng <c>refresh_tokens</c> (ENT-11, Mục 4 + Mục 3.5).</summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.UserId).HasDatabaseName("idx_refresh_user");

        builder.Property(x => x.FamilyId).HasColumnName("family_id");
        // Index một phần: reuse detection chỉ cần tìm token CÒN HIỆU LỰC trong family. Quên HasFilter
        // thì vẫn là index hợp lệ, không lỗi gì — chỉ to hơn cần thiết.
        builder.HasIndex(x => x.FamilyId).HasDatabaseName("idx_refresh_family").HasFilter("revoked_at IS NULL");

        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.TokenHash).IsUnique();

        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");

        builder.Property(x => x.ReplacedById).HasColumnName("replaced_by_id");
        // NO ACTION, đúng DDL Mục 4 (REFERENCES không kèm ON DELETE).
        builder.HasOne<RefreshToken>().WithMany().HasForeignKey(x => x.ReplacedById).OnDelete(DeleteBehavior.NoAction);

        // IPAddress -> inet do Npgsql tự ánh xạ.
        builder.Property(x => x.CreatedIp).HasColumnName("created_ip");

        // Xem ghi chú "Nguồn thời gian" ở RoleConfiguration.
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
    }
}
