using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanionScopedSelfAndProcedural : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_SelfPreferences",
                table: "SelfPreferences");

            migrationBuilder.DropIndex(
                name: "IX_ProceduralRoutines_Trigger",
                table: "ProceduralRoutines");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "SelfPreferences",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "ProceduralRoutines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_SelfPreferences",
                table: "SelfPreferences",
                columns: new[] { "CompanionId", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_SelfPreferences_CompanionId",
                table: "SelfPreferences",
                column: "CompanionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProceduralRoutines_CompanionId_Trigger",
                table: "ProceduralRoutines",
                columns: new[] { "CompanionId", "Trigger" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_SelfPreferences",
                table: "SelfPreferences");

            migrationBuilder.DropIndex(
                name: "IX_SelfPreferences_CompanionId",
                table: "SelfPreferences");

            migrationBuilder.DropIndex(
                name: "IX_ProceduralRoutines_CompanionId_Trigger",
                table: "ProceduralRoutines");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "SelfPreferences");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "ProceduralRoutines");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SelfPreferences",
                table: "SelfPreferences",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_ProceduralRoutines_Trigger",
                table: "ProceduralRoutines",
                column: "Trigger");
        }
    }
}
