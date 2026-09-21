using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialApp.Modules.SocialGraph.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSocialGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "socialgraph");

            migrationBuilder.CreateTable(
                name: "follows",
                schema: "socialgraph",
                columns: table => new
                {
                    follower_id = table.Column<Guid>(type: "uuid", nullable: false),
                    followee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_follows", x => new { x.follower_id, x.followee_id });
                    table.CheckConstraint("ck_follows_not_self", "follower_id <> followee_id");
                });

            migrationBuilder.CreateTable(
                name: "friendships",
                schema: "socialgraph",
                columns: table => new
                {
                    user_min_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_max_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValueSql: "'pending'"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_friendships", x => new { x.user_min_id, x.user_max_id });
                    table.CheckConstraint("ck_friendships_accepted", "(status = 'accepted') = (accepted_at IS NOT NULL)");
                    table.CheckConstraint("ck_friendships_order", "user_min_id < user_max_id");
                    table.CheckConstraint("ck_friendships_requester", "requester_id IN (user_min_id, user_max_id)");
                    table.CheckConstraint("ck_friendships_status", "status IN ('pending','accepted')");
                });

            migrationBuilder.CreateIndex(
                name: "idx_friendships_user_max",
                schema: "socialgraph",
                table: "friendships",
                column: "user_max_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "follows",
                schema: "socialgraph");

            migrationBuilder.DropTable(
                name: "friendships",
                schema: "socialgraph");
        }
    }
}
