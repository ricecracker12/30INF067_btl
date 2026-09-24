using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialApp.Modules.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Gd3Interactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_comments_parent_id",
                schema: "content",
                table: "comments");

            migrationBuilder.AddColumn<string>(
                name: "reaction_counts",
                schema: "content",
                table: "comments",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "reply_count",
                schema: "content",
                table: "comments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "idx_posts_public_recent",
                schema: "content",
                table: "posts",
                columns: new[] { "created_at", "post_id" },
                descending: new bool[0],
                filter: "status = 'published' AND privacy = 'public'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_posts_comment_count",
                schema: "content",
                table: "posts",
                sql: "comment_count >= 0");

            migrationBuilder.CreateIndex(
                name: "idx_comments_parent",
                schema: "content",
                table: "comments",
                columns: new[] { "parent_id", "created_at", "comment_id" });

            migrationBuilder.CreateIndex(
                name: "idx_comments_post_roots",
                schema: "content",
                table: "comments",
                columns: new[] { "post_id", "created_at", "comment_id" },
                filter: "parent_id IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_comments_reply_count",
                schema: "content",
                table: "comments",
                sql: "reply_count >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_comments_root_depth",
                schema: "content",
                table: "comments",
                sql: "(parent_id IS NULL) = (depth = 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_posts_public_recent",
                schema: "content",
                table: "posts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_posts_comment_count",
                schema: "content",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "idx_comments_parent",
                schema: "content",
                table: "comments");

            migrationBuilder.DropIndex(
                name: "idx_comments_post_roots",
                schema: "content",
                table: "comments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_comments_reply_count",
                schema: "content",
                table: "comments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_comments_root_depth",
                schema: "content",
                table: "comments");

            migrationBuilder.DropColumn(
                name: "reaction_counts",
                schema: "content",
                table: "comments");

            migrationBuilder.DropColumn(
                name: "reply_count",
                schema: "content",
                table: "comments");

            migrationBuilder.CreateIndex(
                name: "IX_comments_parent_id",
                schema: "content",
                table: "comments",
                column: "parent_id");
        }
    }
}
