using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Infrastructure.Configurations;

/// <summary>Bảng <c>role_permissions</c> (ENT-10b, Mục 4) — bảng nối thuần, không cột nào khác.</summary>
internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");

        builder.HasKey(x => new { x.RoleId, x.PermissionId });
        builder.Property(x => x.RoleId).HasColumnName("role_id");
        builder.Property(x => x.PermissionId).HasColumnName("permission_id");

        // Xóa vai trò hoặc quyền thì dấu tick tương ứng biến theo — đúng DDL Mục 4.
        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
    }
}
