using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableCompanionRlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    BEGIN
                        EXECUTE format('ALTER ROLE %I SET app.bypass_rls = ''true''', current_user);
                    EXCEPTION WHEN insufficient_privilege THEN
                        NULL;
                    END;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "Companions" ENABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS companions_user_isolation ON "Companions";
                CREATE POLICY companions_user_isolation ON "Companions"
                USING (
                    current_setting('app.bypass_rls', true) = 'true'
                    OR (
                        current_setting('app.current_user_id', true) IS NOT NULL
                        AND current_setting('app.current_user_id', true) <> ''
                        AND "UserId" = current_setting('app.current_user_id', true)
                    )
                )
                WITH CHECK (
                    current_setting('app.bypass_rls', true) = 'true'
                    OR (
                        current_setting('app.current_user_id', true) IS NOT NULL
                        AND current_setting('app.current_user_id', true) <> ''
                        AND "UserId" = current_setting('app.current_user_id', true)
                    )
                );
                """);

            var companionScopedTables = new[]
            {
                "EpisodicMemoryEvents",
                "SemanticClaims",
                "ClaimEvidence",
                "ClaimContradictions",
                "ToolInvocationAudits",
                "ProceduralRoutines",
                "SelfPreferences",
                "ScheduledActions",
                "MemoryRelationships",
                "SubconsciousDebateSessions"
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var companionScopedTables = new[]
            {
                "EpisodicMemoryEvents",
                "SemanticClaims",
                "ClaimEvidence",
                "ClaimContradictions",
                "ToolInvocationAudits",
                "ProceduralRoutines",
                "SelfPreferences",
                "ScheduledActions",
                "MemoryRelationships",
                "SubconsciousDebateSessions"
            };

            foreach (var table in companionScopedTables)
            {
                migrationBuilder.Sql(
                    $"""
                    DROP POLICY IF EXISTS {table.ToLowerInvariant()}_companion_isolation ON "{table}";
                    ALTER TABLE "{table}" DISABLE ROW LEVEL SECURITY;
                    """);
            }

            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS companions_user_isolation ON "Companions";
                ALTER TABLE "Companions" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
