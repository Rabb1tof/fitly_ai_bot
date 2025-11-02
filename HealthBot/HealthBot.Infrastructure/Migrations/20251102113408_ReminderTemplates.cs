using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HealthBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReminderTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reminders_UserId",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "IsSent",
                table: "reminders");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "reminders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "reminders",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastTriggeredAt",
                table: "reminders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextTriggerAt",
                table: "reminders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "RepeatIntervalMinutes",
                table: "reminders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "reminders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "reminder_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DefaultRepeatIntervalMinutes = table.Column<int>(type: "integer", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reminder_templates", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "reminder_templates",
                columns: new[] { "Id", "Code", "DefaultRepeatIntervalMinutes", "Description", "IsSystem", "Title" },
                values: new object[,]
                {
                    { new Guid("4a9df57a-3958-4e32-9b6d-3f5e6f5729fd"), "eat", 180, "Своевременный перекус или обед.", true, "Покушай" },
                    { new Guid("a6df40eb-8138-4949-9305-1c9458c7bf57"), "drink", 45, "Напоминание сделать паузу и выпить воды.", true, "Попей воды!" },
                    { new Guid("d3c63a05-3cb3-4dd0-a6a5-8f3483c962dd"), "stretch", 60, "Короткая разминка, чтобы размять мышцы.", true, "Размяться, хватит сидеть" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_reminders_NextTriggerAt",
                table: "reminders",
                column: "NextTriggerAt");

            migrationBuilder.CreateIndex(
                name: "IX_reminders_TemplateId",
                table: "reminders",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_reminders_UserId_IsActive",
                table: "reminders",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_reminder_templates_Code",
                table: "reminder_templates",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_reminders_reminder_templates_TemplateId",
                table: "reminders",
                column: "TemplateId",
                principalTable: "reminder_templates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reminders_reminder_templates_TemplateId",
                table: "reminders");

            migrationBuilder.DropTable(
                name: "reminder_templates");

            migrationBuilder.DropIndex(
                name: "IX_reminders_NextTriggerAt",
                table: "reminders");

            migrationBuilder.DropIndex(
                name: "IX_reminders_TemplateId",
                table: "reminders");

            migrationBuilder.DropIndex(
                name: "IX_reminders_UserId_IsActive",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "LastTriggeredAt",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "NextTriggerAt",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "RepeatIntervalMinutes",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "reminders");

            migrationBuilder.AddColumn<bool>(
                name: "IsSent",
                table: "reminders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_reminders_UserId",
                table: "reminders",
                column: "UserId");
        }
    }
}
