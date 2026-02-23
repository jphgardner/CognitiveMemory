using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanionScopedCoreMemoryIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ToolInvocationAudits_ToolName_ExecutedAtUtc",
                table: "ToolInvocationAudits");

            migrationBuilder.DropIndex(
                name: "IX_SubconsciousDebateSessions_SessionId_TopicKey_State",
                table: "SubconsciousDebateSessions");

            migrationBuilder.DropIndex(
                name: "IX_SubconsciousDebateSessions_SessionId_TopicKey_TriggerEventId",
                table: "SubconsciousDebateSessions");

            migrationBuilder.DropIndex(
                name: "IX_SemanticClaims_Subject_Predicate_Status",
                table: "SemanticClaims");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledActions_SessionId_CreatedAtUtc",
                table: "ScheduledActions");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledActions_Status_RunAtUtc",
                table: "ScheduledActions");

            migrationBuilder.DropIndex(
                name: "IX_MemoryRelationships_SessionId_FromType_FromId_Status",
                table: "MemoryRelationships");

            migrationBuilder.DropIndex(
                name: "IX_MemoryRelationships_SessionId_FromType_FromId_ToType_ToId_R~",
                table: "MemoryRelationships");

            migrationBuilder.DropIndex(
                name: "IX_MemoryRelationships_SessionId_RelationshipType_Status",
                table: "MemoryRelationships");

            migrationBuilder.DropIndex(
                name: "IX_MemoryRelationships_SessionId_ToType_ToId_Status",
                table: "MemoryRelationships");

            migrationBuilder.DropIndex(
                name: "IX_EpisodicMemoryEvents_SessionId_OccurredAt",
                table: "EpisodicMemoryEvents");

            migrationBuilder.DropIndex(
                name: "IX_ClaimEvidence_ClaimId_CapturedAtUtc",
                table: "ClaimEvidence");

            migrationBuilder.DropIndex(
                name: "IX_ClaimContradictions_ClaimAId_ClaimBId",
                table: "ClaimContradictions");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "ToolInvocationAudits",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "SubconsciousDebateSessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "SemanticClaims",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "ScheduledActions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "MemoryRelationships",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "EpisodicMemoryEvents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "ClaimEvidence",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "ClaimContradictions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_ToolInvocationAudits_CompanionId_ToolName_ExecutedAtUtc",
                table: "ToolInvocationAudits",
                columns: new[] { "CompanionId", "ToolName", "ExecutedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateSessions_CompanionId_SessionId_TopicKey_S~",
                table: "SubconsciousDebateSessions",
                columns: new[] { "CompanionId", "SessionId", "TopicKey", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateSessions_CompanionId_SessionId_TopicKey_T~",
                table: "SubconsciousDebateSessions",
                columns: new[] { "CompanionId", "SessionId", "TopicKey", "TriggerEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemanticClaims_CompanionId_Subject_Predicate_Status",
                table: "SemanticClaims",
                columns: new[] { "CompanionId", "Subject", "Predicate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledActions_CompanionId_SessionId_CreatedAtUtc",
                table: "ScheduledActions",
                columns: new[] { "CompanionId", "SessionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledActions_CompanionId_Status_RunAtUtc",
                table: "ScheduledActions",
                columns: new[] { "CompanionId", "Status", "RunAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRelationships_CompanionId_SessionId_FromType_FromId_S~",
                table: "MemoryRelationships",
                columns: new[] { "CompanionId", "SessionId", "FromType", "FromId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRelationships_CompanionId_SessionId_FromType_FromId_T~",
                table: "MemoryRelationships",
                columns: new[] { "CompanionId", "SessionId", "FromType", "FromId", "ToType", "ToId", "RelationshipType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRelationships_CompanionId_SessionId_RelationshipType_~",
                table: "MemoryRelationships",
                columns: new[] { "CompanionId", "SessionId", "RelationshipType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRelationships_CompanionId_SessionId_ToType_ToId_Status",
                table: "MemoryRelationships",
                columns: new[] { "CompanionId", "SessionId", "ToType", "ToId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EpisodicMemoryEvents_CompanionId_SessionId_OccurredAt",
                table: "EpisodicMemoryEvents",
                columns: new[] { "CompanionId", "SessionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimEvidence_ClaimId",
                table: "ClaimEvidence",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimEvidence_CompanionId_ClaimId_CapturedAtUtc",
                table: "ClaimEvidence",
                columns: new[] { "CompanionId", "ClaimId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimContradictions_CompanionId_ClaimAId_ClaimBId",
                table: "ClaimContradictions",
                columns: new[] { "CompanionId", "ClaimAId", "ClaimBId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ToolInvocationAudits_CompanionId_ToolName_ExecutedAtUtc",
                table: "ToolInvocationAudits");

            migrationBuilder.DropIndex(
                name: "IX_SubconsciousDebateSessions_CompanionId_SessionId_TopicKey_S~",
                table: "SubconsciousDebateSessions");

            migrationBuilder.DropIndex(
                name: "IX_SubconsciousDebateSessions_CompanionId_SessionId_TopicKey_T~",
                table: "SubconsciousDebateSessions");

            migrationBuilder.DropIndex(
                name: "IX_SemanticClaims_CompanionId_Subject_Predicate_Status",
                table: "SemanticClaims");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledActions_CompanionId_SessionId_CreatedAtUtc",
                table: "ScheduledActions");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledActions_CompanionId_Status_RunAtUtc",
                table: "ScheduledActions");

            migrationBuilder.DropIndex(
                name: "IX_MemoryRelationships_CompanionId_SessionId_FromType_FromId_S~",
                table: "MemoryRelationships");

            migrationBuilder.DropIndex(
                name: "IX_MemoryRelationships_CompanionId_SessionId_FromType_FromId_T~",
                table: "MemoryRelationships");

            migrationBuilder.DropIndex(
                name: "IX_MemoryRelationships_CompanionId_SessionId_RelationshipType_~",
                table: "MemoryRelationships");

            migrationBuilder.DropIndex(
                name: "IX_MemoryRelationships_CompanionId_SessionId_ToType_ToId_Status",
                table: "MemoryRelationships");

            migrationBuilder.DropIndex(
                name: "IX_EpisodicMemoryEvents_CompanionId_SessionId_OccurredAt",
                table: "EpisodicMemoryEvents");

            migrationBuilder.DropIndex(
                name: "IX_ClaimEvidence_ClaimId",
                table: "ClaimEvidence");

            migrationBuilder.DropIndex(
                name: "IX_ClaimEvidence_CompanionId_ClaimId_CapturedAtUtc",
                table: "ClaimEvidence");

            migrationBuilder.DropIndex(
                name: "IX_ClaimContradictions_CompanionId_ClaimAId_ClaimBId",
                table: "ClaimContradictions");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "ToolInvocationAudits");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "SubconsciousDebateSessions");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "SemanticClaims");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "ScheduledActions");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "MemoryRelationships");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "EpisodicMemoryEvents");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "ClaimEvidence");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "ClaimContradictions");

            migrationBuilder.CreateIndex(
                name: "IX_ToolInvocationAudits_ToolName_ExecutedAtUtc",
                table: "ToolInvocationAudits",
                columns: new[] { "ToolName", "ExecutedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateSessions_SessionId_TopicKey_State",
                table: "SubconsciousDebateSessions",
                columns: new[] { "SessionId", "TopicKey", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateSessions_SessionId_TopicKey_TriggerEventId",
                table: "SubconsciousDebateSessions",
                columns: new[] { "SessionId", "TopicKey", "TriggerEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemanticClaims_Subject_Predicate_Status",
                table: "SemanticClaims",
                columns: new[] { "Subject", "Predicate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledActions_SessionId_CreatedAtUtc",
                table: "ScheduledActions",
                columns: new[] { "SessionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledActions_Status_RunAtUtc",
                table: "ScheduledActions",
                columns: new[] { "Status", "RunAtUtc" });

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

            migrationBuilder.CreateIndex(
                name: "IX_EpisodicMemoryEvents_SessionId_OccurredAt",
                table: "EpisodicMemoryEvents",
                columns: new[] { "SessionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimEvidence_ClaimId_CapturedAtUtc",
                table: "ClaimEvidence",
                columns: new[] { "ClaimId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimContradictions_ClaimAId_ClaimBId",
                table: "ClaimContradictions",
                columns: new[] { "ClaimAId", "ClaimBId" },
                unique: true);
        }
    }
}
