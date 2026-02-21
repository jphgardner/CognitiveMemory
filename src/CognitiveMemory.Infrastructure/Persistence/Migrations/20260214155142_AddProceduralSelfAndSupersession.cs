using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProceduralSelfAndSupersession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SupersededByClaimId",
                table: "SemanticClaims",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProceduralRoutines",
                columns: table => new
                {
                    RoutineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StepsJson = table.Column<string>(type: "text", nullable: false),
                    CheckpointsJson = table.Column<string>(type: "text", nullable: false),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProceduralRoutines", x => x.RoutineId);
                });

            migrationBuilder.CreateTable(
                name: "SelfPreferences",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfPreferences", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticClaims_SupersededByClaimId",
                table: "SemanticClaims",
                column: "SupersededByClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_ProceduralRoutines_Trigger",
                table: "ProceduralRoutines",
                column: "Trigger");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProceduralRoutines");

            migrationBuilder.DropTable(
                name: "SelfPreferences");

            migrationBuilder.DropIndex(
                name: "IX_SemanticClaims_SupersededByClaimId",
                table: "SemanticClaims");

            migrationBuilder.DropColumn(
                name: "SupersededByClaimId",
                table: "SemanticClaims");
        }
    }
}
