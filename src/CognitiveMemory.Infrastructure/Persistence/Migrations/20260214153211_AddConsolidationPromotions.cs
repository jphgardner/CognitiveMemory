using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsolidationPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsolidationPromotions",
                columns: table => new
                {
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodicEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SemanticClaimId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidationPromotions", x => x.PromotionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationPromotions_EpisodicEventId",
                table: "ConsolidationPromotions",
                column: "EpisodicEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationPromotions_ProcessedAtUtc",
                table: "ConsolidationPromotions",
                column: "ProcessedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsolidationPromotions");
        }
    }
}
