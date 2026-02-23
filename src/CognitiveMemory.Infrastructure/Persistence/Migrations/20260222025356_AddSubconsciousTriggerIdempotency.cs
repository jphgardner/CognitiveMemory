using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubconsciousTriggerIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateSessions_SessionId_TopicKey_TriggerEventId",
                table: "SubconsciousDebateSessions",
                columns: new[] { "SessionId", "TopicKey", "TriggerEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubconsciousDebateSessions_SessionId_TopicKey_TriggerEventId",
                table: "SubconsciousDebateSessions");
        }
    }
}
