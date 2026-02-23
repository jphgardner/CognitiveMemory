using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemoryRelationshipsGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemoryRelationships",
                columns: table => new
                {
                    RelationshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FromType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ToType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ToId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RelationshipType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    Strength = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValidToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryRelationships", x => x.RelationshipId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRelationships_SessionId_FromType_FromId_Status",
                table: "MemoryRelationships",
                columns: new[] { "SessionId", "FromType", "FromId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRelationships_SessionId_FromType_FromId_ToType_ToId_R~",
                table: "MemoryRelationships",
                columns: new[] { "SessionId", "FromType", "FromId", "ToType", "ToId", "RelationshipType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRelationships_SessionId_RelationshipType_Status",
                table: "MemoryRelationships",
                columns: new[] { "SessionId", "RelationshipType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRelationships_SessionId_ToType_ToId_Status",
                table: "MemoryRelationships",
                columns: new[] { "SessionId", "ToType", "ToId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemoryRelationships");
        }
    }
}
