using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CognitiveMemory.Api.Tests;

public sealed class SubconsciousDebateEndpointsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task RunOnce_QueuesDebate_AndCanBeListedBySession()
    {
        var client = factory.CreateClient();
        var sessionId = $"test-session-{Guid.NewGuid():N}";
        await AuthenticateAndCreateCompanionAsync(client, sessionId);

        var response = await client.PostAsJsonAsync(
            "/api/subconscious/debates/run-once",
            new
            {
                sessionId,
                topicKey = "manual-check",
                triggerEventType = "ManualRun",
                triggerPayloadJson = "{}"
            });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var rows = await client.GetFromJsonAsync<SubconsciousDebateSessionEntity[]>($"/api/subconscious/debates/{sessionId}?take=20");
        Assert.NotNull(rows);
        Assert.Contains(rows!, x => x.TopicKey == "manual-check");
    }

    [Fact]
    public async Task Reject_TransitionsDebateToCompleted()
    {
        var debateId = Guid.NewGuid();
        var sessionId = $"reject-session-{Guid.NewGuid():N}";
        var client = factory.CreateClient();
        var companion = await AuthenticateAndCreateCompanionAsync(client, sessionId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MemoryDbContext>();
            var now = DateTimeOffset.UtcNow;

            db.SubconsciousDebateSessions.Add(
                new SubconsciousDebateSessionEntity
                {
                    DebateId = debateId,
                    CompanionId = companion.CompanionId,
                    SessionId = sessionId,
                    TopicKey = "identity-evolution",
                    TriggerEventType = "ManualRun",
                    TriggerPayloadJson = "{}",
                    State = "AwaitingUser",
                    Priority = 50,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

            db.SubconsciousDebateOutcomes.Add(
                new SubconsciousDebateOutcomeEntity
                {
                    DebateId = debateId,
                    OutcomeJson = "{\"decisionType\":\"needs_user_input\",\"finalConfidence\":0.4,\"reasoningSummary\":\"manual\",\"evidenceRefs\":[],\"claimsToCreate\":[],\"claimsToSupersede\":[],\"contradictions\":[],\"proceduralUpdates\":[],\"selfUpdates\":[],\"requiresUserInput\":true,\"userQuestion\":\"confirm?\"}",
                    OutcomeHash = "h",
                    ValidationStatus = "NeedsUserConfirmation",
                    ApplyStatus = "Pending",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

            await db.SaveChangesAsync();
        }
        var response = await client.PostAsync($"/api/subconscious/debates/{debateId}/reject", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var assertScope = factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<MemoryDbContext>();
        var row = await assertDb.SubconsciousDebateSessions.FindAsync(debateId);
        Assert.NotNull(row);
        Assert.Equal("Completed", row!.State);
    }

    [Fact]
    public async Task Stream_ReturnsSseContentType()
    {
        var client = factory.CreateClient();
        var sessionId = $"stream-session-{Guid.NewGuid():N}";
        await AuthenticateAndCreateCompanionAsync(client, sessionId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/subconscious/debates/stream?sessionId={sessionId}&take=10");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<CompanionDto> AuthenticateAndCreateCompanionAsync(HttpClient client, string sessionId)
    {
        var email = $"sub-{Guid.NewGuid():N}@test.local";
        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "password123!" });
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var create = await client.PostAsJsonAsync(
            "/api/companions",
            new
            {
                name = "Sub Test",
                tone = "friendly",
                purpose = "sub test",
                modelHint = "openai:gpt-4.1-mini",
                sessionId
            });
        create.EnsureSuccessStatusCode();
        var companion = await create.Content.ReadFromJsonAsync<CompanionDto>();
        Assert.NotNull(companion);
        return companion!;
    }

    private sealed record AuthResponseDto(string AccessToken, DateTimeOffset ExpiresAtUtc, object User);
    private sealed record CompanionDto(Guid CompanionId, string SessionId);
}
