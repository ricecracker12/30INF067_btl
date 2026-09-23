using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialApp.Modules.Notification.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notification");

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    group_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    reason_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                    table.UniqueConstraint("uq_notifications_group", x => new { x.recipient_id, x.group_key });
                    table.CheckConstraint("ck_notifications_actor_count", "actor_count >= 1");
                    table.CheckConstraint("ck_notifications_type", "type IN ('comment','reply','reaction','friend_request','friend_accepted','message','moderation','tag')");
                });

            migrationBuilder.CreateTable(
                name: "notification_actors",
                schema: "notification",
                columns: table => new
                {
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_actors", x => new { x.notification_id, x.actor_id });
                    table.ForeignKey(
                        name: "FK_notification_actors_notifications_notification_id",
                        column: x => x.notification_id,
                        principalSchema: "notification",
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_notifications_recent",
                schema: "notification",
                table: "notifications",
                columns: new[] { "recipient_id", "updated_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "idx_notifications_unread",
                schema: "notification",
                table: "notifications",
                column: "recipient_id",
                filter: "is_read = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_actors",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notification");
        }
    }
}
