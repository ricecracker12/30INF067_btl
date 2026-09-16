using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Infrastructure.Configurations;

/// <summary>Bảng <c>roles</c> (ENT-10, Mục 4).</summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(x => x.RoleId);
        // Id gán tay 1/2/3 theo Mục 5.1 — role_permissions trỏ vào đúng các số này. Thiếu dòng này
        // EF sinh cột identity và seeder chèn id tường minh sẽ lệch sequence.
        builder.Property(x => x.RoleId).HasColumnName("role_id").ValueGeneratedNever();

        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();

        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(120);

        // Nguồn thời gian là đồng hồ app (initializer trong entity). DEFAULT now() chỉ là lưới an toàn
        // cho INSERT bằng SQL thô — chính là đường seeder A4 đi. Xem Mục 4 "Nguồn thời gian".
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
    }
}
