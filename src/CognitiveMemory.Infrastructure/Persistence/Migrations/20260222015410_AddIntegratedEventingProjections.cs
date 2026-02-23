using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegratedEventingProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConflictEscalationAlerts",
                columns: table => new
                {
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Predicate = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ValuesJson = table.Column<string>(type: "text", nullable: false),
                    ContradictionCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConflictEscalationAlerts", x => x.AlertId);
                });

            migrationBuilder.CreateTable(
                name: "ProceduralRoutineMetrics",
                columns: table => new
                {
                    RoutineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SuccessCount = table.Column<int>(type: "integer", nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    LastOutcomeSummary = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProceduralRoutineMetrics", x => x.RoutineId);
                });

            migrationBuilder.CreateTable(
                name: "UserProfileProjections",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfileProjections", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConflictEscalationAlerts_Subject_Predicate_Status",
                table: "ConflictEscalationAlerts",
                columns: new[] { "Subject", "Predicate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProceduralRoutineMetrics_Trigger",
                table: "ProceduralRoutineMetrics",
                column: "Trigger");

            migrationBuilder.CreateIndex(
                name: "IX_ProceduralRoutineMetrics_UpdatedAtUtc",
                table: "ProceduralRoutineMetrics",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfileProjections_UpdatedAtUtc",
                table: "UserProfileProjections",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConflictEscalationAlerts");

            migrationBuilder.DropTable(
                name: "ProceduralRoutineMetrics");

            migrationBuilder.DropTable(
                name: "UserProfileProjections");
        }
    }
}
