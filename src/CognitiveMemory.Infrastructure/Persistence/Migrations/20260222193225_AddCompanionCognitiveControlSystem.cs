using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanionCognitiveControlSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserProfileProjections",
                table: "UserProfileProjections");

            migrationBuilder.DropIndex(
                name: "IX_UserProfileProjections_UpdatedAtUtc",
                table: "UserProfileProjections");

            migrationBuilder.DropIndex(
                name: "IX_SubconsciousDebateTurns_CreatedAtUtc",
                table: "SubconsciousDebateTurns");

            migrationBuilder.DropIndex(
                name: "IX_SubconsciousDebateTurns_DebateId_TurnNumber",
                table: "SubconsciousDebateTurns");

            migrationBuilder.DropIndex(
                name: "IX_SubconsciousDebateOutcomes_OutcomeHash",
                table: "SubconsciousDebateOutcomes");

            migrationBuilder.DropIndex(
                name: "IX_ProceduralRoutineMetrics_Trigger",
                table: "ProceduralRoutineMetrics");

            migrationBuilder.DropIndex(
                name: "IX_ProceduralRoutineMetrics_UpdatedAtUtc",
                table: "ProceduralRoutineMetrics");

            migrationBuilder.DropIndex(
                name: "IX_ConflictEscalationAlerts_Subject_Predicate_Status",
                table: "ConflictEscalationAlerts");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "UserProfileProjections",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "SubconsciousDebateTurns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "SubconsciousDebateOutcomes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "SubconsciousDebateMetrics",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "ProceduralRoutineMetrics",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanionId",
                table: "ConflictEscalationAlerts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveCognitiveProfileVersionId",
                table: "Companions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserProfileProjections",
                table: "UserProfileProjections",
                columns: new[] { "CompanionId", "Key" });

            migrationBuilder.CreateTable(
                name: "CompanionCognitiveProfileAudits",
                columns: table => new
                {
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromProfileVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToProfileVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DiffJson = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanionCognitiveProfileAudits", x => x.AuditId);
                    table.ForeignKey(
                        name: "FK_CompanionCognitiveProfileAudits_Companions_CompanionId",
                        column: x => x.CompanionId,
                        principalTable: "Companions",
                        principalColumn: "CompanionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanionCognitiveProfiles",
                columns: table => new
                {
                    CompanionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveProfileVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StagedProfileVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanionCognitiveProfiles", x => x.CompanionId);
                    table.ForeignKey(
                        name: "FK_CompanionCognitiveProfiles_Companions_CompanionId",
                        column: x => x.CompanionId,
                        principalTable: "Companions",
                        principalColumn: "CompanionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanionCognitiveProfileVersions",
                columns: table => new
                {
                    ProfileVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfileJson = table.Column<string>(type: "text", nullable: false),
                    CompiledRuntimeJson = table.Column<string>(type: "text", nullable: false),
                    ProfileHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ValidationStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ChangeSummary = table.Column<string>(type: "text", nullable: true),
                    ChangeReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanionCognitiveProfileVersions", x => x.ProfileVersionId);
                    table.ForeignKey(
                        name: "FK_CompanionCognitiveProfileVersions_Companions_CompanionId",
                        column: x => x.CompanionId,
                        principalTable: "Companions",
                        principalColumn: "CompanionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanionCognitiveRuntimeTraces",
                columns: table => new
                {
                    TraceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProfileVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestCorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Phase = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DecisionJson = table.Column<string>(type: "text", nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanionCognitiveRuntimeTraces", x => x.TraceId);
                    table.ForeignKey(
                        name: "FK_CompanionCognitiveRuntimeTraces_Companions_CompanionId",
                        column: x => x.CompanionId,
                        principalTable: "Companions",
                        principalColumn: "CompanionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserProfileProjections_CompanionId_UpdatedAtUtc",
                table: "UserProfileProjections",
                columns: new[] { "CompanionId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateTurns_CompanionId_CreatedAtUtc",
                table: "SubconsciousDebateTurns",
                columns: new[] { "CompanionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateTurns_CompanionId_DebateId_TurnNumber",
                table: "SubconsciousDebateTurns",
                columns: new[] { "CompanionId", "DebateId", "TurnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateOutcomes_CompanionId_OutcomeHash",
                table: "SubconsciousDebateOutcomes",
                columns: new[] { "CompanionId", "OutcomeHash" });

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateMetrics_CompanionId_CreatedAtUtc",
                table: "SubconsciousDebateMetrics",
                columns: new[] { "CompanionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProceduralRoutineMetrics_CompanionId_Trigger",
                table: "ProceduralRoutineMetrics",
                columns: new[] { "CompanionId", "Trigger" });

            migrationBuilder.CreateIndex(
                name: "IX_ProceduralRoutineMetrics_CompanionId_UpdatedAtUtc",
                table: "ProceduralRoutineMetrics",
                columns: new[] { "CompanionId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ConflictEscalationAlerts_CompanionId_Subject_Predicate_Stat~",
                table: "ConflictEscalationAlerts",
                columns: new[] { "CompanionId", "Subject", "Predicate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Companions_ActiveCognitiveProfileVersionId",
                table: "Companions",
                column: "ActiveCognitiveProfileVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanionCognitiveProfileAudits_CompanionId_Action_CreatedA~",
                table: "CompanionCognitiveProfileAudits",
                columns: new[] { "CompanionId", "Action", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionCognitiveProfileAudits_CompanionId_CreatedAtUtc",
                table: "CompanionCognitiveProfileAudits",
                columns: new[] { "CompanionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionCognitiveProfiles_ActiveProfileVersionId",
                table: "CompanionCognitiveProfiles",
                column: "ActiveProfileVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanionCognitiveProfiles_StagedProfileVersionId",
                table: "CompanionCognitiveProfiles",
                column: "StagedProfileVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanionCognitiveProfiles_UpdatedAtUtc",
                table: "CompanionCognitiveProfiles",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CompanionCognitiveProfileVersions_CompanionId_CreatedAtUtc",
                table: "CompanionCognitiveProfileVersions",
                columns: new[] { "CompanionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionCognitiveProfileVersions_CompanionId_ProfileHash",
                table: "CompanionCognitiveProfileVersions",
                columns: new[] { "CompanionId", "ProfileHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanionCognitiveProfileVersions_CompanionId_ValidationSta~",
                table: "CompanionCognitiveProfileVersions",
                columns: new[] { "CompanionId", "ValidationStatus", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionCognitiveProfileVersions_CompanionId_VersionNumber",
                table: "CompanionCognitiveProfileVersions",
                columns: new[] { "CompanionId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanionCognitiveRuntimeTraces_CompanionId_CreatedAtUtc",
                table: "CompanionCognitiveRuntimeTraces",
                columns: new[] { "CompanionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionCognitiveRuntimeTraces_CompanionId_ProfileVersionI~",
                table: "CompanionCognitiveRuntimeTraces",
                columns: new[] { "CompanionId", "ProfileVersionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionCognitiveRuntimeTraces_CompanionId_RequestCorrelat~",
                table: "CompanionCognitiveRuntimeTraces",
                columns: new[] { "CompanionId", "RequestCorrelationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionCognitiveRuntimeTraces_CompanionId_SessionId_Creat~",
                table: "CompanionCognitiveRuntimeTraces",
                columns: new[] { "CompanionId", "SessionId", "CreatedAtUtc" });

            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    UPDATE "SubconsciousDebateTurns" t
                    SET "CompanionId" = s."CompanionId"
                    FROM "SubconsciousDebateSessions" s
                    WHERE t."DebateId" = s."DebateId"
                      AND t."CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid;

                    UPDATE "SubconsciousDebateOutcomes" o
                    SET "CompanionId" = s."CompanionId"
                    FROM "SubconsciousDebateSessions" s
                    WHERE o."DebateId" = s."DebateId"
                      AND o."CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid;

                    UPDATE "SubconsciousDebateMetrics" m
                    SET "CompanionId" = s."CompanionId"
                    FROM "SubconsciousDebateSessions" s
                    WHERE m."DebateId" = s."DebateId"
                      AND m."CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid;

                    UPDATE "ProceduralRoutineMetrics" m
                    SET "CompanionId" = r."CompanionId"
                    FROM "ProceduralRoutines" r
                    WHERE m."RoutineId" = r."RoutineId"
                      AND m."CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid;
                    """);

                migrationBuilder.Sql(
                    """
                    WITH projection_candidates AS (
                        SELECT
                            up."Key",
                            sp."CompanionId",
                            ROW_NUMBER() OVER (
                                PARTITION BY up."Key"
                                ORDER BY sp."UpdatedAtUtc" DESC
                            ) AS rn
                        FROM "UserProfileProjections" up
                        INNER JOIN "SelfPreferences" sp ON sp."Key" = up."Key"
                        WHERE up."CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid
                    )
                    UPDATE "UserProfileProjections" up
                    SET "CompanionId" = c."CompanionId"
                    FROM projection_candidates c
                    WHERE up."Key" = c."Key"
                      AND c.rn = 1
                      AND up."CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid;

                    WITH conflict_candidates AS (
                        SELECT
                            a."AlertId",
                            sc."CompanionId",
                            ROW_NUMBER() OVER (
                                PARTITION BY a."AlertId"
                                ORDER BY sc."UpdatedAtUtc" DESC, sc."CreatedAtUtc" DESC
                            ) AS rn
                        FROM "ConflictEscalationAlerts" a
                        INNER JOIN "SemanticClaims" sc
                            ON sc."Subject" = a."Subject"
                           AND sc."Predicate" = a."Predicate"
                        WHERE a."CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid
                    )
                    UPDATE "ConflictEscalationAlerts" a
                    SET "CompanionId" = c."CompanionId"
                    FROM conflict_candidates c
                    WHERE a."AlertId" = c."AlertId"
                      AND c.rn = 1
                      AND a."CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE "UserProfileProjections"
                    SET "CompanionId" = (
                        SELECT c."CompanionId"
                        FROM "Companions" c
                        WHERE c."IsArchived" = FALSE
                        LIMIT 1
                    )
                    WHERE "CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid
                      AND (SELECT COUNT(*) FROM "Companions" c WHERE c."IsArchived" = FALSE) = 1;

                    UPDATE "SubconsciousDebateTurns"
                    SET "CompanionId" = (
                        SELECT c."CompanionId"
                        FROM "Companions" c
                        WHERE c."IsArchived" = FALSE
                        LIMIT 1
                    )
                    WHERE "CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid
                      AND (SELECT COUNT(*) FROM "Companions" c WHERE c."IsArchived" = FALSE) = 1;

                    UPDATE "SubconsciousDebateOutcomes"
                    SET "CompanionId" = (
                        SELECT c."CompanionId"
                        FROM "Companions" c
                        WHERE c."IsArchived" = FALSE
                        LIMIT 1
                    )
                    WHERE "CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid
                      AND (SELECT COUNT(*) FROM "Companions" c WHERE c."IsArchived" = FALSE) = 1;

                    UPDATE "SubconsciousDebateMetrics"
                    SET "CompanionId" = (
                        SELECT c."CompanionId"
                        FROM "Companions" c
                        WHERE c."IsArchived" = FALSE
                        LIMIT 1
                    )
                    WHERE "CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid
                      AND (SELECT COUNT(*) FROM "Companions" c WHERE c."IsArchived" = FALSE) = 1;

                    UPDATE "ProceduralRoutineMetrics"
                    SET "CompanionId" = (
                        SELECT c."CompanionId"
                        FROM "Companions" c
                        WHERE c."IsArchived" = FALSE
                        LIMIT 1
                    )
                    WHERE "CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid
                      AND (SELECT COUNT(*) FROM "Companions" c WHERE c."IsArchived" = FALSE) = 1;

                    UPDATE "ConflictEscalationAlerts"
                    SET "CompanionId" = (
                        SELECT c."CompanionId"
                        FROM "Companions" c
                        WHERE c."IsArchived" = FALSE
                        LIMIT 1
                    )
                    WHERE "CompanionId" = '00000000-0000-0000-0000-000000000000'::uuid
                      AND (SELECT COUNT(*) FROM "Companions" c WHERE c."IsArchived" = FALSE) = 1;
                    """);

                migrationBuilder.Sql(
                    """
                    ALTER TABLE "UserProfileProjections" ALTER COLUMN "CompanionId" DROP DEFAULT;
                    ALTER TABLE "SubconsciousDebateTurns" ALTER COLUMN "CompanionId" DROP DEFAULT;
                    ALTER TABLE "SubconsciousDebateOutcomes" ALTER COLUMN "CompanionId" DROP DEFAULT;
                    ALTER TABLE "SubconsciousDebateMetrics" ALTER COLUMN "CompanionId" DROP DEFAULT;
                    ALTER TABLE "ProceduralRoutineMetrics" ALTER COLUMN "CompanionId" DROP DEFAULT;
                    ALTER TABLE "ConflictEscalationAlerts" ALTER COLUMN "CompanionId" DROP DEFAULT;
                    """);

                var companionScopedTables = new[]
                {
                    "UserProfileProjections",
                    "SubconsciousDebateTurns",
                    "SubconsciousDebateOutcomes",
                    "SubconsciousDebateMetrics",
                    "ProceduralRoutineMetrics",
                    "ConflictEscalationAlerts",
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                var companionScopedTables = new[]
                {
                    "UserProfileProjections",
                    "SubconsciousDebateTurns",
                    "SubconsciousDebateOutcomes",
                    "SubconsciousDebateMetrics",
                    "ProceduralRoutineMetrics",
                    "ConflictEscalationAlerts",
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

            migrationBuilder.DropTable(
                name: "CompanionCognitiveProfileAudits");

            migrationBuilder.DropTable(
                name: "CompanionCognitiveProfiles");

            migrationBuilder.DropTable(
                name: "CompanionCognitiveProfileVersions");

            migrationBuilder.DropTable(
                name: "CompanionCognitiveRuntimeTraces");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserProfileProjections",
                table: "UserProfileProjections");

            migrationBuilder.DropIndex(
                name: "IX_UserProfileProjections_CompanionId_UpdatedAtUtc",
                table: "UserProfileProjections");

            migrationBuilder.DropIndex(
                name: "IX_SubconsciousDebateTurns_CompanionId_CreatedAtUtc",
                table: "SubconsciousDebateTurns");

            migrationBuilder.DropIndex(
                name: "IX_SubconsciousDebateTurns_CompanionId_DebateId_TurnNumber",
                table: "SubconsciousDebateTurns");

            migrationBuilder.DropIndex(
                name: "IX_SubconsciousDebateOutcomes_CompanionId_OutcomeHash",
                table: "SubconsciousDebateOutcomes");

            migrationBuilder.DropIndex(
                name: "IX_SubconsciousDebateMetrics_CompanionId_CreatedAtUtc",
                table: "SubconsciousDebateMetrics");

            migrationBuilder.DropIndex(
                name: "IX_ProceduralRoutineMetrics_CompanionId_Trigger",
                table: "ProceduralRoutineMetrics");

            migrationBuilder.DropIndex(
                name: "IX_ProceduralRoutineMetrics_CompanionId_UpdatedAtUtc",
                table: "ProceduralRoutineMetrics");

            migrationBuilder.DropIndex(
                name: "IX_ConflictEscalationAlerts_CompanionId_Subject_Predicate_Stat~",
                table: "ConflictEscalationAlerts");

            migrationBuilder.DropIndex(
                name: "IX_Companions_ActiveCognitiveProfileVersionId",
                table: "Companions");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "UserProfileProjections");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "SubconsciousDebateTurns");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "SubconsciousDebateOutcomes");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "SubconsciousDebateMetrics");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "ProceduralRoutineMetrics");

            migrationBuilder.DropColumn(
                name: "CompanionId",
                table: "ConflictEscalationAlerts");

            migrationBuilder.DropColumn(
                name: "ActiveCognitiveProfileVersionId",
                table: "Companions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserProfileProjections",
                table: "UserProfileProjections",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfileProjections_UpdatedAtUtc",
                table: "UserProfileProjections",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateTurns_CreatedAtUtc",
                table: "SubconsciousDebateTurns",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateTurns_DebateId_TurnNumber",
                table: "SubconsciousDebateTurns",
                columns: new[] { "DebateId", "TurnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubconsciousDebateOutcomes_OutcomeHash",
                table: "SubconsciousDebateOutcomes",
                column: "OutcomeHash");

            migrationBuilder.CreateIndex(
                name: "IX_ProceduralRoutineMetrics_Trigger",
                table: "ProceduralRoutineMetrics",
                column: "Trigger");

            migrationBuilder.CreateIndex(
                name: "IX_ProceduralRoutineMetrics_UpdatedAtUtc",
                table: "ProceduralRoutineMetrics",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ConflictEscalationAlerts_Subject_Predicate_Status",
                table: "ConflictEscalationAlerts",
                columns: new[] { "Subject", "Predicate", "Status" });
        }
    }
}
