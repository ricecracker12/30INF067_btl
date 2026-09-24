using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialApp.Modules.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PublicRecentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_posts_public_recent",
                schema: "content",
                table: "posts",
                columns: new[] { "created_at", "post_id" },
                descending: new[] { true, true },
                filter: "status = 'published' AND privacy = 'public'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_posts_public_recent",
                schema: "content",
                table: "posts");
        }
    }
}
