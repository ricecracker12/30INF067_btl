using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialApp.Modules.Messaging.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq_counter = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    last_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    user_a_delivered_seq = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    user_a_seen_seq = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    user_b_delivered_seq = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    user_b_seen_seq = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.id);
                    table.CheckConstraint("ck_conversations_marks", "user_a_seen_seq <= user_a_delivered_seq AND user_a_delivered_seq <= seq_counter AND user_b_seen_seq <= user_b_delivered_seq AND user_b_delivered_seq <= seq_counter");
                    table.CheckConstraint("ck_conversations_order", "user_a_id < user_b_id");
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    client_msg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.CheckConstraint("ck_messages_content", "char_length(content) BETWEEN 1 AND 2000 AND btrim(content) <> ''");
                    table.CheckConstraint("ck_messages_seq_positive", "seq > 0");
                    table.ForeignKey(
                        name: "fk_messages_conversation",
                        column: x => x.conversation_id,
                        principalSchema: "messaging",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_conversations_a_recent",
                schema: "messaging",
                table: "conversations",
                columns: new[] { "user_a_id", "last_message_at", "id" },
                descending: new[] { false, true, true },
                filter: "last_message_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_conversations_b_recent",
                schema: "messaging",
                table: "conversations",
                columns: new[] { "user_b_id", "last_message_at", "id" },
                descending: new[] { false, true, true },
                filter: "last_message_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_conversations_pair",
                schema: "messaging",
                table: "conversations",
                columns: new[] { "user_a_id", "user_b_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_messages_conv_client_id",
                schema: "messaging",
                table: "messages",
                columns: new[] { "conversation_id", "client_msg_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_messages_conv_seq",
                schema: "messaging",
                table: "messages",
                columns: new[] { "conversation_id", "seq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "messaging");
        }
    }
}
