using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialApp.Modules.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "content");

            migrationBuilder.CreateTable(
                name: "media_attachments",
                schema: "content",
                columns: table => new
                {
                    media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<short>(type: "smallint", nullable: true),
                    height = table.Column<short>(type: "smallint", nullable: true),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_attachments", x => x.media_id);
                    table.CheckConstraint("ck_media_content_type", "content_type IN ('image/jpeg','image/png','image/webp')");
                    table.CheckConstraint("ck_media_owner_type", "owner_type IN ('post','message')");
                    table.CheckConstraint("ck_media_position", "position BETWEEN 0 AND 9");
                    table.CheckConstraint("ck_media_size", "size_bytes > 0 AND size_bytes <= 10485760");
                });

            migrationBuilder.CreateTable(
                name: "posts",
                schema: "content",
                columns: table => new
                {
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    privacy = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValueSql: "'public'"),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValueSql: "'published'"),
                    media_count = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    comment_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    reaction_counts = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    hidden_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_posts", x => x.post_id);
                    table.CheckConstraint("ck_posts_media_count", "media_count BETWEEN 0 AND 10");
                    table.CheckConstraint("ck_posts_not_empty", "media_count > 0 OR btrim(coalesce(body,'')) <> ''");
                    table.CheckConstraint("ck_posts_privacy", "privacy IN ('public','friends','private')");
                    table.CheckConstraint("ck_posts_status", "status IN ('published','hidden','deleted')");
                });

            migrationBuilder.CreateTable(
                name: "reactions",
                schema: "content",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reactions", x => new { x.user_id, x.target_type, x.target_id });
                    table.CheckConstraint("ck_reactions_target", "target_type IN ('post','comment')");
                    table.CheckConstraint("ck_reactions_type", "type IN ('like','love','haha','wow','sad','angry')");
                });

            migrationBuilder.CreateTable(
                name: "comments",
                schema: "content",
                columns: table => new
                {
                    comment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depth = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValueSql: "'visible'"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comments", x => x.comment_id);
                    table.CheckConstraint("ck_comments_depth", "depth BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_comments_status", "status IN ('visible','deleted')");
                    table.ForeignKey(
                        name: "FK_comments_comments_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "content",
                        principalTable: "comments",
                        principalColumn: "comment_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_comments_posts_post_id",
                        column: x => x.post_id,
                        principalSchema: "content",
                        principalTable: "posts",
                        principalColumn: "post_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_comments_parent_id",
                schema: "content",
                table: "comments",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_comments_post_id",
                schema: "content",
                table: "comments",
                column: "post_id");

            migrationBuilder.CreateIndex(
                name: "idx_media_owner",
                schema: "content",
                table: "media_attachments",
                columns: new[] { "owner_type", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "IX_media_attachments_storage_key",
                schema: "content",
                table: "media_attachments",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_media_owner_position",
                schema: "content",
                table: "media_attachments",
                columns: new[] { "owner_type", "owner_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_posts_author_created",
                schema: "content",
                table: "posts",
                columns: new[] { "author_id", "created_at", "post_id" },
                descending: new[] { false, true, true },
                filter: "status = 'published'");

            migrationBuilder.CreateIndex(
                name: "idx_reactions_target",
                schema: "content",
                table: "reactions",
                columns: new[] { "target_type", "target_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comments",
                schema: "content");

            migrationBuilder.DropTable(
                name: "media_attachments",
                schema: "content");

            migrationBuilder.DropTable(
                name: "reactions",
                schema: "content");

            migrationBuilder.DropTable(
                name: "posts",
                schema: "content");
        }
    }
}
