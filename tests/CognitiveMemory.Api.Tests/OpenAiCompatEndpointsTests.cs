using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CognitiveMemory.Api.Tests;

public sealed class OpenAiCompatEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public OpenAiCompatEndpointsTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ModelsEndpointReturnsOpenAiShape()
    {
        var response = await _client.GetAsync("/v1/models");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("object", out var objectEl));
        Assert.Equal("list", objectEl.GetString());
        Assert.True(payload.TryGetProperty("data", out _));
    }

    [Fact]
    public async Task ChatCompletionsReturnsOpenAiShape()
    {
        var request = new
        {
            model = "cognitivememory-chat",
            messages = new[]
            {
                new { role = "system", content = "You are helpful." },
                new { role = "user", content = "What did we decide?" }
            },
            stream = false
        };

        var response = await _client.PostAsJsonAsync("/v1/chat/completions", request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("id", out _));
        Assert.True(payload.TryGetProperty("object", out var objectEl));
        Assert.Equal("chat.completion", objectEl.GetString());
        Assert.True(payload.TryGetProperty("choices", out var choices));
        Assert.True(choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0);

        var first = choices[0];
        Assert.True(first.TryGetProperty("message", out var message));
        Assert.True(message.TryGetProperty("content", out _));
    }

    [Fact]
    public async Task ChatCompletionsRejectsMissingUserMessage()
    {
        var request = new
        {
            model = "cognitivememory-chat",
            messages = new[]
            {
                new { role = "system", content = "You are helpful." }
            },
            stream = false
        };

        var response = await _client.PostAsJsonAsync("/v1/chat/completions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("error", out _));
    }
}
