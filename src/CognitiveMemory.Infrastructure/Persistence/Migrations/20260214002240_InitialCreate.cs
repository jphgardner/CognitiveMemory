using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClaimCalibrations",
                columns: table => new
                {
                    CalibrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecommendedConfidence = table.Column<double>(type: "double precision", nullable: false),
                    SourceEventRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReasonCodesJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimCalibrations", x => x.CalibrationId);
                });

            migrationBuilder.CreateTable(
                name: "ClaimInsights",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    KeywordsJson = table.Column<string>(type: "text", nullable: false),
                    SourceEventRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimInsights", x => x.ClaimId);
                });

            migrationBuilder.CreateTable(
                name: "Claims",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Predicate = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ObjectEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    LiteralValue = table.Column<string>(type: "text", nullable: true),
                    ValueType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValidTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastReinforcedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Claims", x => x.ClaimId);
                    table.CheckConstraint("ck_claim_object_or_literal", "(\"ObjectEntityId\" IS NULL) <> (\"LiteralValue\" IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "Contradictions",
                columns: table => new
                {
                    ContradictionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimAId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimBId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contradictions", x => x.ContradictionId);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Metadata = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.DocumentId);
                });

            migrationBuilder.CreateTable(
                name: "Entities",
                columns: table => new
                {
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Aliases = table.Column<string>(type: "jsonb", nullable: false),
                    Metadata = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entities", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "PolicyDecisions",
                columns: table => new
                {
                    DecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RiskScore = table.Column<double>(type: "double precision", nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReasonCodesJson = table.Column<string>(type: "text", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyDecisions", x => x.DecisionId);
                });

            migrationBuilder.CreateTable(
                name: "ToolExecutions",
                columns: table => new
                {
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToolName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolExecutions", x => x.ExecutionId);
                });

            migrationBuilder.CreateTable(
                name: "Evidence",
                columns: table => new
                {
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExcerptOrSummary = table.Column<string>(type: "text", nullable: false),
                    Strength = table.Column<double>(type: "double precision", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Metadata = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evidence", x => x.EvidenceId);
                    table.ForeignKey(
                        name: "FK_Evidence_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimCalibrations_ClaimId_CreatedAt",
                table: "ClaimCalibrations",
                columns: new[] { "ClaimId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimInsights_UpdatedAt",
                table: "ClaimInsights",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_Hash",
                table: "Claims",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contradictions_ClaimAId_ClaimBId",
                table: "Contradictions",
                columns: new[] { "ClaimAId", "ClaimBId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_SourceType_SourceRef_ContentHash",
                table: "Documents",
                columns: new[] { "SourceType", "SourceRef", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Entities_Type_Name",
                table: "Entities",
                columns: new[] { "Type", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_ClaimId",
                table: "Evidence",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_EventType_IdempotencyKey",
                table: "OutboxEvents",
                columns: new[] { "EventType", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_Status_AvailableAt",
                table: "OutboxEvents",
                columns: new[] { "Status", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDecisions_SourceType_SourceRef_CreatedAt",
                table: "PolicyDecisions",
                columns: new[] { "SourceType", "SourceRef", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ToolExecutions_ToolName_IdempotencyKey",
                table: "ToolExecutions",
                columns: new[] { "ToolName", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimCalibrations");

            migrationBuilder.DropTable(
                name: "ClaimInsights");

            migrationBuilder.DropTable(
                name: "Contradictions");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Entities");

            migrationBuilder.DropTable(
                name: "Evidence");

            migrationBuilder.DropTable(
                name: "OutboxEvents");

            migrationBuilder.DropTable(
                name: "PolicyDecisions");

            migrationBuilder.DropTable(
                name: "ToolExecutions");

            migrationBuilder.DropTable(
                name: "Claims");
        }
    }
}
