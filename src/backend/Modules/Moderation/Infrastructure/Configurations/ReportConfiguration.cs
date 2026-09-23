using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialApp.Modules.Moderation.Domain;

namespace SocialApp.Modules.Moderation.Infrastructure.Configurations;

/// <summary>
/// Ánh xạ <see cref="Report"/> → <c>moderation.reports</c>, đúng DDL giai-doan-6.md Mục 4. Năm CHECK + ba index giao luật của
/// Đ-6.12/Đ-6.13 cho Postgres giữ — kể cả SQL thô của D6/D7 cũng không đi vòng được.
///
/// CHECK tập giá trị dựng từ hằng của Domain (khuôn <c>UserConfiguration.StatusCheckSql</c>): hằng C# và ràng buộc DB không lệch
/// nhau được. Không FK nào (Đ-2.2).
/// </summary>
internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    private const string OpenFilter = $"status = '{ReportStatus.Open}'";

    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("reports", t =>
        {
            t.HasCheckConstraint("ck_reports_target_type", InSql("target_type", ReportTargetTypes.All));
            t.HasCheckConstraint("ck_reports_reason", InSql("reason_code", ReasonCodes.All));
            t.HasCheckConstraint("ck_reports_status", InSql("status", ReportStatus.All));
            t.HasCheckConstraint("ck_reports_other_detail", $"reason_code <> '{ReasonCodes.Other}' OR detail IS NOT NULL");
            // Một chiều, có người quyết: open ⇔ chưa có resolver lẫn resolved_at.
            t.HasCheckConstraint("ck_reports_decided",
                $"({OpenFilter}) = (resolved_at IS NULL AND resolver_id IS NULL)");
        });

        builder.HasKey(x => x.Id);
        // UUID v7 do app sinh — không để EF chen generator thứ hai vào.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.ReporterId).HasColumnName("reporter_id");
        builder.Property(x => x.TargetType).HasColumnName("target_type").HasMaxLength(10).IsRequired();
        builder.Property(x => x.TargetId).HasColumnName("target_id");
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(500);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(10).IsRequired()
            .HasDefaultValue(ReportStatus.Open);
        builder.Property(x => x.ResolverId).HasColumnName("resolver_id");
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(x => x.ResolutionNote).HasColumnName("resolution_note").HasMaxLength(500);

        // Nguồn thời gian là đồng hồ app; DEFAULT now() chỉ là lưới cho INSERT bằng SQL thô (D6 đi đường đó).
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        // Đ-6.12: một báo cáo MỞ mỗi (người báo, đối tượng). D6 phải viết ON CONFLICT với ĐÚNG vế WHERE này:
        //   ON CONFLICT (reporter_id, target_type, target_id) WHERE status = 'open' DO NOTHING
        // Thiếu vế WHERE thì Postgres không suy ra được index → 42P10 lúc chạy (Mục 4 chỗ dễ sai 1).
        builder.HasIndex(x => new { x.ReporterId, x.TargetType, x.TargetId })
            .IsUnique()
            .HasFilter(OpenFilter)
            .HasDatabaseName("uq_reports_open_per_reporter");

        // Đ-6.13: hàng đợi — báo cáo mở, cũ nhất trước.
        builder.HasIndex(x => new { x.CreatedAt, x.Id })
            .HasFilter(OpenFilter)
            .HasDatabaseName("idx_reports_open_queue");

        // Đ-6.13 bước 3: đóng mọi báo cáo mở của một đối tượng + lịch sử xử lý của đối tượng.
        builder.HasIndex(x => new { x.TargetType, x.TargetId, x.Status })
            .HasDatabaseName("idx_reports_target");
    }

    private static string InSql(string column, IEnumerable<string> values) =>
        $"{column} IN ({string.Join(",", values.Select(v => $"'{v}'"))})";
}
