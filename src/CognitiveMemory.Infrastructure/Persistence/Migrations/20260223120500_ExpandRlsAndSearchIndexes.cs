using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    public partial class ExpandRlsAndSearchIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS pg_trgm;""");

            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_OutboxMessages_PayloadJson_Trgm" ON "OutboxMessages" USING gin ("PayloadJson" gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_ToolInvocationAudits_ArgumentsJson_Trgm" ON "ToolInvocationAudits" USING gin ("ArgumentsJson" gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_ToolInvocationAudits_ResultJson_Trgm" ON "ToolInvocationAudits" USING gin ("ResultJson" gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_SemanticClaims_Subject_Trgm" ON "SemanticClaims" USING gin ("Subject" gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_SemanticClaims_Predicate_Trgm" ON "SemanticClaims" USING gin ("Predicate" gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_SemanticClaims_Value_Trgm" ON "SemanticClaims" USING gin ("Value" gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_SemanticClaims_Scope_Trgm" ON "SemanticClaims" USING gin ("Scope" gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_ProceduralRoutines_Trigger_Trgm" ON "ProceduralRoutines" USING gin ("Trigger" gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_ProceduralRoutines_Name_Trgm" ON "ProceduralRoutines" USING gin ("Name" gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_ProceduralRoutines_Outcome_Trgm" ON "ProceduralRoutines" USING gin ("Outcome" gin_trgm_ops);""");

            var companionScopedTables = new[]
            {
                "ConflictEscalationAlerts",
                "UserProfileProjections",
                "ProceduralRoutineMetrics",
                "SubconsciousDebateTurns",
                "SubconsciousDebateOutcomes",
                "SubconsciousDebateMetrics",
                "CompanionCognitiveProfiles",
                "CompanionCognitiveProfileVersions",
                "CompanionCognitiveProfileAudits",
                "CompanionCognitiveRuntimeTraces"
            };

            foreach (var table in companionScopedTables)
            {
                migrationBuilder.Sql(
                    $"""
                    ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS {table.ToLowerInvariant()}_companion_isolation ON "{table}";
                    CREATE POLICY {table.ToLowerInvariant()}_companion_isolation ON "{table}"
                    USING (
                        current_setting('app.bypass_rls', true) = 'true'
                        OR EXISTS (
                            SELECT 1
                            FROM "Companions" c
                            WHERE c."CompanionId" = "{table}"."CompanionId"
                              AND c."IsArchived" = FALSE
                              AND c."UserId" = current_setting('app.current_user_id', true)
                        )
                    )
                    WITH CHECK (
                        current_setting('app.bypass_rls', true) = 'true'
                        OR EXISTS (
                            SELECT 1
                            FROM "Companions" c
                            WHERE c."CompanionId" = "{table}"."CompanionId"
                              AND c."IsArchived" = FALSE
                              AND c."UserId" = current_setting('app.current_user_id', true)
                        )
                    );
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_ProceduralRoutines_Outcome_Trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_ProceduralRoutines_Name_Trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_ProceduralRoutines_Trigger_Trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_SemanticClaims_Scope_Trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_SemanticClaims_Value_Trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_SemanticClaims_Predicate_Trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_SemanticClaims_Subject_Trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_ToolInvocationAudits_ResultJson_Trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_ToolInvocationAudits_ArgumentsJson_Trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_OutboxMessages_PayloadJson_Trgm";""");

            var companionScopedTables = new[]
            {
                "ConflictEscalationAlerts",
                "UserProfileProjections",
                "ProceduralRoutineMetrics",
                "SubconsciousDebateTurns",
                "SubconsciousDebateOutcomes",
                "SubconsciousDebateMetrics",
                "CompanionCognitiveProfiles",
                "CompanionCognitiveProfileVersions",
                "CompanionCognitiveProfileAudits",
                "CompanionCognitiveRuntimeTraces"
            };

            foreach (var table in companionScopedTables)
            {
                migrationBuilder.Sql(
                    $"""
                    DROP POLICY IF EXISTS {table.ToLowerInvariant()}_companion_isolation ON "{table}";
                    ALTER TABLE "{table}" DISABLE ROW LEVEL SECURITY;
                    """);
            }
        }
    }
}
