using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CognitiveMemory.Api.Tests;

public sealed class EndpointsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task ChatEndpoint_ReturnsOk()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/chat", new { message = "hello" });

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
        var response = await client.PostAsJsonAsync("/api/chat/stream", new { message = "hello" });

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
}
