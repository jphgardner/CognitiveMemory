using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CognitiveMemory.Api.Tests;

public sealed class AuthWorkspaceEndpointsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task Register_Then_CreateCompanion_Then_GetWorkspaceSummary_ReturnsOk()
    {
        var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "password123!" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var auth = await register.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var create = await client.PostAsJsonAsync(
            "/api/companions",
            new
            {
                name = "Nova",
                tone = "friendly",
                purpose = "Test companion",
                modelHint = "openai:gpt-4.1-mini",
                originStory = "Created for integration testing.",
                initialMemoryText = "Remember that I prefer concise answers."
            });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var companion = await create.Content.ReadFromJsonAsync<CompanionDto>();
        Assert.NotNull(companion);

        var summary = await client.GetAsync($"/api/workspace/companion/{companion!.CompanionId}/summary");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
    }

    [Fact]
    public async Task WorkspaceEndpoints_RejectCrossUserCompanionAccess()
    {
        var clientA = factory.CreateClient();
        var authA = await Register(clientA, $"a-{Guid.NewGuid():N}@test.local");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authA.AccessToken);

        var created = await clientA.PostAsJsonAsync(
            "/api/companions",
            new
            {
                name = "Private Companion",
                tone = "analyst",
                purpose = "Ownership test",
                modelHint = "openai:gpt-4.1-mini"
            });
        var companion = await created.Content.ReadFromJsonAsync<CompanionDto>();
        Assert.NotNull(companion);

        var clientB = factory.CreateClient();
        var authB = await Register(clientB, $"b-{Guid.NewGuid():N}@test.local");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authB.AccessToken);

        var denied = await clientB.GetAsync($"/api/workspace/companion/{companion!.CompanionId}/summary");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task AuthMe_ReturnsCurrentUser()
    {
        var client = factory.CreateClient();
        var auth = await Register(client, $"me-{Guid.NewGuid():N}@test.local");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    private static async Task<AuthResponseDto> Register(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "password123!" });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        return auth!;
    }

    private sealed record AuthResponseDto(string AccessToken, DateTimeOffset ExpiresAtUtc, AuthUserDto User);
    private sealed record AuthUserDto(Guid UserId, string Email, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
    private sealed record CompanionDto(Guid CompanionId);
}
