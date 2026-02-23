using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledActions",
                columns: table => new
                {
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InputJson = table.Column<string>(type: "text", nullable: false),
                    RunAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledActions", x => x.ActionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledActions_SessionId_CreatedAtUtc",
                table: "ScheduledActions",
                columns: new[] { "SessionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledActions_Status_RunAtUtc",
                table: "ScheduledActions",
                columns: new[] { "Status", "RunAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledActions");
        }
    }
}
