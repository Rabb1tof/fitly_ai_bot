using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConversationSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversation_sessions",
                columns: table => new
                {
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    flow = table.Column<int>(type: "integer", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    template_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    template_default_repeat = table.Column<int>(type: "integer", nullable: true),
                    custom_message = table.Column<string>(type: "text", nullable: true),
                    first_delay_minutes = table.Column<int>(type: "integer", nullable: true),
                    expect_manual_input = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    last_bot_message_id = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_sessions", x => x.chat_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conversation_sessions_updated_at",
                table: "conversation_sessions",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_sessions");
        }
    }
}
