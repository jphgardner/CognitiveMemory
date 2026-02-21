using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSemanticMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClaimContradictions",
                columns: table => new
                {
                    ContradictionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimAId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimBId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimContradictions", x => x.ContradictionId);
                });

            migrationBuilder.CreateTable(
                name: "SemanticClaims",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Predicate = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    Scope = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValidToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticClaims", x => x.ClaimId);
                });

            migrationBuilder.CreateTable(
                name: "ClaimEvidence",
                columns: table => new
                {
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExcerptOrSummary = table.Column<string>(type: "text", nullable: false),
                    Strength = table.Column<double>(type: "double precision", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimEvidence", x => x.EvidenceId);
                    table.ForeignKey(
                        name: "FK_ClaimEvidence_SemanticClaims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "SemanticClaims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimContradictions_ClaimAId_ClaimBId",
                table: "ClaimContradictions",
                columns: new[] { "ClaimAId", "ClaimBId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimEvidence_ClaimId_CapturedAtUtc",
                table: "ClaimEvidence",
                columns: new[] { "ClaimId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticClaims_Subject_Predicate_Status",
                table: "SemanticClaims",
                columns: new[] { "Subject", "Predicate", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimContradictions");

            migrationBuilder.DropTable(
                name: "ClaimEvidence");

            migrationBuilder.DropTable(
                name: "SemanticClaims");
        }
    }
}
