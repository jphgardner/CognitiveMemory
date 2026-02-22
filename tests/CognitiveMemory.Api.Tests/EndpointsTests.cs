using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace CognitiveMemory.Api.Tests;

public sealed class EndpointsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task ChatEndpoint_ReturnsOk()
    {
        var client = factory.CreateClient();
        var companion = await AuthenticateAndCreateCompanionAsync(client);
        var response = await client.PostAsJsonAsync("/api/chat", new { companionId = companion.CompanionId, message = "hello" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ConsolidationRunOnce_ReturnsOk()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/consolidation/run-once", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChatStreamEndpoint_ReturnsEventStream()
    {
        var client = factory.CreateClient();
        var companion = await AuthenticateAndCreateCompanionAsync(client);
        var response = await client.PostAsJsonAsync("/api/chat/stream", new { companionId = companion.CompanionId, message = "hello" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ReasoningRunOnce_ReturnsOk()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/reasoning/run-once", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlanningGenerate_ReturnsOk()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/planning/goals",
            new { sessionId = "s1", goal = "stabilize api deployment", lookbackDays = 14, maxSteps = 6 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task IdentityRunOnce_ReturnsOk()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/identity/run-once", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TruthRunOnce_ReturnsOk()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/truth/run-once", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlanningOutcome_ReturnsOk()
    {
        var client = factory.CreateClient();
        var planId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            $"/api/planning/goals/{planId}/outcome",
            new
            {
                sessionId = "s1",
                goal = "stabilize api deployment",
                succeeded = true,
                executedSteps = new[] { "step 1", "step 2" },
                outcomeSummary = "completed"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MemoryRelationships_CreateAndQuery_ReturnsOk()
    {
        var client = factory.CreateClient();
        var companion = await AuthenticateAndCreateCompanionAsync(client);
        var claimA = Guid.NewGuid().ToString("N");
        var claimB = Guid.NewGuid().ToString("N");

        var create = await client.PostAsJsonAsync(
            "/api/relationships",
            new
            {
                companionId = companion.CompanionId,
                sessionId = companion.SessionId,
                fromType = 0,
                fromId = claimA,
                toType = 0,
                toId = claimB,
                relationshipType = "supports",
                confidence = 0.8,
                strength = 0.7
            });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var query = await client.GetAsync($"/api/relationships/by-session?companionId={companion.CompanionId}&sessionId={companion.SessionId}&take=20");
        Assert.Equal(HttpStatusCode.OK, query.StatusCode);
    }

    [Fact]
    public async Task MemoryRelationships_Backfill_ReturnsOk()
    {
        var client = factory.CreateClient();
        var companion = await AuthenticateAndCreateCompanionAsync(client);
        var response = await client.PostAsJsonAsync(
            "/api/relationships/backfill/run-once",
            new { sessionId = companion.SessionId, take = 500 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MemoryRelationships_ExtractRunOnce_ReturnsOk()
    {
        var client = factory.CreateClient();
        var companion = await AuthenticateAndCreateCompanionAsync(client);
        var response = await client.PostAsJsonAsync(
            "/api/relationships/extract/run-once",
            new { sessionId = companion.SessionId, take = 200, apply = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CompanionCognitiveProfile_LifecycleEndpoints_ReturnOk()
    {
        var client = factory.CreateClient();
        var companion = await AuthenticateAndCreateCompanionAsync(client);

        var get = await client.GetAsync($"/api/companions/{companion.CompanionId}/cognitive-profile");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var stateEnvelope = await get.Content.ReadFromJsonAsync<CognitiveProfileStateEnvelopeDto>();
        Assert.NotNull(stateEnvelope);
        Assert.NotNull(stateEnvelope!.State);

        var create = await client.PostAsJsonAsync(
            $"/api/companions/{companion.CompanionId}/cognitive-profile/versions",
            new
            {
                profile = new
                {
                    schemaVersion = "1.0.0",
                    attention = new
                    {
                        focusStickiness = 0.7,
                        explorationBreadth = 2,
                        clarificationFrequency = 0.3,
                        contextWindowAllocation = new { working = 0.34, episodic = 0.2, semantic = 0.24, procedural = 0.12, self = 0.1 }
                    },
                    memory = new
                    {
                        retrievalWeights = new { recency = 0.8, semanticMatch = 1.0, evidenceStrength = 0.7, relationshipDegree = 0.45, confidence = 0.65 },
                        layerPriorities = new { working = 0.2, episodic = 0.4, semantic = 0.6, procedural = 0.45, self = 0.5, identityBoost = 0.9 },
                        maxCandidates = 120,
                        maxResults = 20,
                        dedupeSensitivity = 0.6,
                        writeThresholds = new { confidenceMin = 0.62, importanceMin = 0.55 },
                        decay = new { semanticDailyDecay = 0.02, episodicDailyDecay = 0.04, reinforcementMultiplier = 1.2 }
                    },
                    reasoning = new { reasoningMode = "hybrid", structureTemplate = "evidence-first", depth = 2, evidenceStrictness = 0.7 },
                    expression = new { verbosityTarget = "balanced", toneStyle = "professional", emotionalExpressivity = 0.2, formatRigidity = 0.55 },
                    reflection = new
                    {
                        selfCritiqueEnabled = true,
                        selfCritiqueRate = 0.25,
                        maxSelfCritiquePasses = 1,
                        debate = new { triggerSensitivity = 0.55, turnCap = 8, terminationConfidenceThreshold = 0.78, convergenceDeltaMin = 0.02 }
                    },
                    uncertainty = new
                    {
                        answerConfidenceThreshold = 0.66,
                        clarifyConfidenceThreshold = 0.5,
                        deferConfidenceThreshold = 0.3,
                        conflictEscalationThreshold = 0.74,
                        requireCitationsInHighRiskDomains = true
                    },
                    adaptation = new { procedurality = 0.58, adaptivity = 0.42, policyStrictness = 0.65 },
                    evolution = new
                    {
                        evolutionMode = "propose-only",
                        maxDailyDelta = 0.06,
                        learningSignals = new { userSatisfaction = true, hallucinationDetections = true, clarificationRate = true, latencyBreaches = true },
                        approvalPolicy = "human-required"
                    }
                },
                changeSummary = "test update",
                changeReason = "integration test"
            });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var createdVersion = await create.Content.ReadFromJsonAsync<CognitiveProfileVersionDto>();
        Assert.NotNull(createdVersion);

        var activate = await client.PostAsJsonAsync(
            $"/api/companions/{companion.CompanionId}/cognitive-profile/activate",
            new { profileVersionId = createdVersion!.ProfileVersionId, reason = "activate test version" });
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        var versions = await client.GetAsync($"/api/companions/{companion.CompanionId}/cognitive-profile/versions");
        Assert.Equal(HttpStatusCode.OK, versions.StatusCode);
        var versionRows = await versions.Content.ReadFromJsonAsync<List<CognitiveProfileVersionDto>>();
        Assert.NotNull(versionRows);
        Assert.True(versionRows!.Count >= 1);

        var rollback = await client.PostAsJsonAsync(
            $"/api/companions/{companion.CompanionId}/cognitive-profile/rollback",
            new { targetProfileVersionId = stateEnvelope.State.ActiveProfileVersionId, reason = "rollback test" });
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
    }

    private static async Task<CompanionDto> AuthenticateAndCreateCompanionAsync(HttpClient client)
    {
        var email = $"endpoint-{Guid.NewGuid():N}@test.local";
        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "password123!" });
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var create = await client.PostAsJsonAsync(
            "/api/companions",
            new
            {
                name = "Endpoint Companion",
                tone = "friendly",
                purpose = "Endpoint tests",
                modelHint = "openai:gpt-4.1-mini"
            });
        create.EnsureSuccessStatusCode();
        var companion = await create.Content.ReadFromJsonAsync<CompanionDto>();
        Assert.NotNull(companion);
        return companion!;
    }

    private sealed record AuthResponseDto(string AccessToken, DateTimeOffset ExpiresAtUtc, object User);
    private sealed record CompanionDto(Guid CompanionId, string SessionId);
    private sealed record CognitiveProfileStateEnvelopeDto(CognitiveProfileStateDto State, CognitiveProfileVersionDto? Active);
    private sealed record CognitiveProfileStateDto(Guid CompanionId, Guid ActiveProfileVersionId, Guid? StagedProfileVersionId, int ActiveVersionNumber, string SchemaVersion, string ValidationStatus, DateTimeOffset UpdatedAtUtc, string UpdatedByUserId);
    private sealed record CognitiveProfileVersionDto(Guid ProfileVersionId, Guid CompanionId, int VersionNumber, string SchemaVersion, string ValidationStatus, string ProfileHash, string CreatedByUserId, string? ChangeSummary, string? ChangeReason, DateTimeOffset CreatedAtUtc, string ProfileJson, string CompiledRuntimeJson);
}
