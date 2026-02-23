using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubconsciousDebateSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubconsciousDebateMetrics",
                columns: table => new
                {
                    DebateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnCount = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    ConvergenceScore = table.Column<double>(type: "double precision", nullable: false),
                    ContradictionsDetected = table.Column<int>(type: "integer", nullable: false),
                    ClaimsProposed = table.Column<int>(type: "integer", nullable: false),
                    ClaimsApplied = table.Column<int>(type: "integer", nullable: false),
                    RequiresUserInput = table.Column<bool>(type: "boolean", nullable: false),
                    FinalConfidence = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubconsciousDebateMetrics", x => x.DebateId);
                });

            migrationBuilder.CreateTable(
                name: "SubconsciousDebateOutcomes",
                columns: table => new
                {
                    DebateId = table.Column<Guid>(type: "uuid", nullable: false),
                    OutcomeJson = table.Column<string>(type: "text", nullable: false),
                    OutcomeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ValidationStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApplyStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApplyError = table.Column<string>(type: "text", nullable: true),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubconsciousDebateOutcomes", x => x.DebateId);
                });

            migrationBuilder.CreateTable(
                name: "SubconsciousDebateSessions",
                columns: table => new
                {
                    DebateId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TopicKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TriggerEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    TriggerEventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TriggerPayloadJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubconsciousDebateSessions", x => x.DebateId);
                });

            migrationBuilder.CreateTable(
                name: "SubconsciousDebateTurns",
                columns: table => new
                {
                    TurnId = table.Column<Guid>(type: "uuid", nullable: false),
                    DebateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnNumber = table.Column<int>(type: "integer", nullable: false),
                    AgentName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    StructuredPayloadJson = table.Column<string>(type: "text", nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubconsciousDebateTurns", x => x.TurnId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateOutcomes_OutcomeHash",
                table: "SubconsciousDebateOutcomes",
                column: "OutcomeHash");

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateSessions_CreatedAtUtc",
                table: "SubconsciousDebateSessions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateSessions_SessionId_TopicKey_State",
                table: "SubconsciousDebateSessions",
                columns: new[] { "SessionId", "TopicKey", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateSessions_TriggerEventId",
                table: "SubconsciousDebateSessions",
                column: "TriggerEventId");

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateTurns_CreatedAtUtc",
                table: "SubconsciousDebateTurns",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateTurns_DebateId_TurnNumber",
                table: "SubconsciousDebateTurns",
                columns: new[] { "DebateId", "TurnNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubconsciousDebateMetrics");

            migrationBuilder.DropTable(
                name: "SubconsciousDebateOutcomes");

            migrationBuilder.DropTable(
                name: "SubconsciousDebateSessions");

            migrationBuilder.DropTable(
                name: "SubconsciousDebateTurns");
        }
    }
}
