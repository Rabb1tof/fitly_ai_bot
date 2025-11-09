using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuietHoursFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "quiet_hours_end_minutes",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quiet_hours_start_minutes",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_quiet_hours_end_minutes",
                table: "conversation_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_quiet_hours_start_minutes",
                table: "conversation_sessions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "quiet_hours_end_minutes",
                table: "users");

            migrationBuilder.DropColumn(
                name: "quiet_hours_start_minutes",
                table: "users");

            migrationBuilder.DropColumn(
                name: "pending_quiet_hours_end_minutes",
                table: "conversation_sessions");

            migrationBuilder.DropColumn(
                name: "pending_quiet_hours_start_minutes",
                table: "conversation_sessions");
        }
    }
}
