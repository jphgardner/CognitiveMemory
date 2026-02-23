using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSemanticClaimEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SemanticClaimEmbeddings",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    VectorJson = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EmbeddedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticClaimEmbeddings", x => x.ClaimId);
                    table.ForeignKey(
                        name: "FK_SemanticClaimEmbeddings_SemanticClaims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "SemanticClaims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticClaimEmbeddings_EmbeddedAtUtc",
                table: "SemanticClaimEmbeddings",
                column: "EmbeddedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SemanticClaimEmbeddings");
        }
    }
}
