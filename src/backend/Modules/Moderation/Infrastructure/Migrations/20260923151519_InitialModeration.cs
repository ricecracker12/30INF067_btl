using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SocialApp.Modules.Moderation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "moderation");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "moderation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                schema: "moderation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "open"),
                    resolver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.id);
                    table.CheckConstraint("ck_reports_decided", "(status = 'open') = (resolved_at IS NULL AND resolver_id IS NULL)");
                    table.CheckConstraint("ck_reports_other_detail", "reason_code <> 'other' OR detail IS NOT NULL");
                    table.CheckConstraint("ck_reports_reason", "reason_code IN ('spam','harassment','nudity','violence','other')");
                    table.CheckConstraint("ck_reports_status", "status IN ('open','resolved','dismissed')");
                    table.CheckConstraint("ck_reports_target_type", "target_type IN ('post','comment','user')");
                });

            migrationBuilder.CreateIndex(
                name: "idx_audit_logs_actor",
                schema: "moderation",
                table: "audit_logs",
                columns: new[] { "actor_id", "id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_audit_logs_target",
                schema: "moderation",
                table: "audit_logs",
                columns: new[] { "target_type", "target_id", "id" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "idx_reports_open_queue",
                schema: "moderation",
                table: "reports",
                columns: new[] { "created_at", "id" },
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "idx_reports_target",
                schema: "moderation",
                table: "reports",
                columns: new[] { "target_type", "target_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_reports_open_per_reporter",
                schema: "moderation",
                table: "reports",
                columns: new[] { "reporter_id", "target_type", "target_id" },
                unique: true,
                filter: "status = 'open'");

            // Đ-6.15: audit_logs append-only do DB giữ, không do quy ước code. App dùng một user DB là chủ bảng nên REVOKE
            // không có tác dụng; trigger chặn mọi code vô tình — muốn lách phải DROP TRIGGER, một thao tác lộ liễu.
            // Cửa duy nhất: DELETE khi phiên đặt SET LOCAL socialapp.audit_purge = 'on' (job xóa 12 tháng của GĐ8). Không bao
            // giờ có cửa cho UPDATE. TRUNCATE chặn bằng trigger riêng mức câu lệnh — trigger mức dòng không thấy TRUNCATE
            // (lệch Mục 4, chốt 2026-09-23 — L-A6 của hướng dẫn khối A+C).
            migrationBuilder.Sql("""
                CREATE FUNCTION moderation.audit_logs_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND current_setting('socialapp.audit_purge', true) = 'on' THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'moderation.audit_logs là append-only (%)', TG_OP USING ERRCODE = 'P0001';
                END $$;

                CREATE TRIGGER trg_audit_logs_append_only BEFORE UPDATE OR DELETE ON moderation.audit_logs
                    FOR EACH ROW EXECUTE FUNCTION moderation.audit_logs_append_only();

                CREATE TRIGGER trg_audit_logs_no_truncate BEFORE TRUNCATE ON moderation.audit_logs
                    FOR EACH STATEMENT EXECUTE FUNCTION moderation.audit_logs_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_audit_logs_no_truncate ON moderation.audit_logs;
                DROP TRIGGER IF EXISTS trg_audit_logs_append_only ON moderation.audit_logs;
                DROP FUNCTION IF EXISTS moderation.audit_logs_append_only();
                """);

            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "moderation");

            migrationBuilder.DropTable(
                name: "reports",
                schema: "moderation");
        }
    }
}
