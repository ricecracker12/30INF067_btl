using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Infrastructure.Configurations;

/// <summary>Bảng <c>permissions</c> (ENT-10a, Mục 4).</summary>
internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");

        builder.HasKey(x => x.PermissionId);
        // Id gán tay 1..17 theo Mục 5.2, cùng lý do như roles.role_id.
        builder.Property(x => x.PermissionId).HasColumnName("permission_id").ValueGeneratedNever();

        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();

        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(120);
    }
}
