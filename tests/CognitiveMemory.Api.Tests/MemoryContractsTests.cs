using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CognitiveMemory.Application.Contracts;

namespace CognitiveMemory.Api.Tests;

public sealed class MemoryContractsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MemoryContractsTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpointReturnsExpectedContractShape()
    {
        var response = await _client.GetAsync("/api/v1/memory/health");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(payload.TryGetProperty("database", out _));
        Assert.True(payload.TryGetProperty("cache", out _));
        Assert.True(payload.TryGetProperty("cacheLatencyMs", out _));
        Assert.True(payload.TryGetProperty("model", out _));
        Assert.True(payload.TryGetProperty("modelProvider", out _));
    }

    [Fact]
    public async Task CreateClaimReturnsCreatedStatusCode()
    {
        var request = new CreateClaimRequest
        {
            SubjectEntityId = Guid.NewGuid(),
            Predicate = "selected_transport",
            LiteralValue = "SignalR",
            ValueType = "String",
            Confidence = 0.82,
            Hash = "hash-1",
            Evidence =
            [
                new CreateEvidenceRequest
                {
                    SourceType = "ChatTurn",
                    SourceRef = "conv:1/turn:2",
                    ExcerptOrSummary = "We selected SignalR.",
                    Strength = 0.8
                }
            ]
        };

        var response = await _client.PostAsJsonAsync("/api/v1/memory/claims", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task IngestReturnsAcceptedStatusCode()
    {
        var request = new IngestDocumentRequest
        {
            SourceType = "ChatTurn",
            SourceRef = "conv:1/turn:1",
            Content = "We switched to SignalR.",
            Metadata = new Dictionary<string, string>
            {
                ["project"] = "PokemonMMO"
            }
        };

        var response = await _client.PostAsJsonAsync("/api/v1/ingest", request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task QueryReturnsContractShape()
    {
        var request = new QueryClaimsRequest
        {
            Text = "What transport did we choose?",
            Filters = new QueryFilters { Subject = "Project:PokemonMMO" },
            TopK = 10,
            IncludeEvidence = true,
            IncludeContradictions = true
        };

        var response = await _client.PostAsJsonAsync("/api/v1/query", request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("claims", out _));
        Assert.True(payload.TryGetProperty("meta", out var meta));
        Assert.True(meta.TryGetProperty("strategy", out _));
        Assert.True(meta.TryGetProperty("latencyMs", out _));
        Assert.True(meta.TryGetProperty("requestId", out _));
    }

    [Fact]
    public async Task QueryRejectsInvalidTopKWithErrorShape()
    {
        var request = new QueryClaimsRequest
        {
            Text = "What transport did we choose?",
            TopK = 0
        };

        var response = await _client.PostAsJsonAsync("/api/v1/query", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("error", out var error));
        Assert.True(error.TryGetProperty("code", out _));
        Assert.True(error.TryGetProperty("message", out _));
        Assert.True(payload.TryGetProperty("requestId", out _));
    }

    [Fact]
    public async Task AnswerReturnsContractShape()
    {
        var request = new AnswerQuestionRequest
        {
            Question = "What did we decide?",
            Context = new Dictionary<string, string>
            {
                ["project"] = "PokemonMMO"
            }
        };

        var response = await _client.PostAsJsonAsync("/api/v1/answer", request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("answer", out _));
        Assert.True(payload.TryGetProperty("confidence", out _));
        Assert.True(payload.TryGetProperty("citations", out _));
        Assert.True(payload.TryGetProperty("uncertaintyFlags", out _));
        Assert.True(payload.TryGetProperty("contradictions", out _));
        Assert.True(payload.TryGetProperty("conscience", out _));
        Assert.True(payload.TryGetProperty("requestId", out _));
    }
}
