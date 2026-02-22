using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CognitiveMemory.Application.Cognitive;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using CognitiveMemory.Infrastructure.SemanticKernel.Plugins;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Api.Endpoints;

public static class CompanionEndpoints
{
    public static IEndpointRouteBuilder MapCompanionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/companions").WithTags("Companions").RequireAuthorization();

        group.MapGet(
            "/",
            async (HttpContext httpContext, bool? includeArchived, MemoryDbContext dbContext, CancellationToken cancellationToken) =>
            {
                var userId = ResolveUserId(httpContext);
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Results.Unauthorized();
                }

                var query = dbContext.Companions.AsNoTracking().Where(x => x.UserId == userId);
                if (includeArchived != true)
                {
                    query = query.Where(x => !x.IsArchived);
                }

                var rows = await query
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .Select(x => ToDto(x))
                    .ToListAsync(cancellationToken);

                return Results.Ok(rows);
            });

        group.MapPost(
            "/",
            async (
                HttpContext httpContext,
                CreateCompanionRequest request,
                MemoryDbContext dbContext,
                MemoryToolsPlugin memoryToolsPlugin,
                ICompanionCognitiveProfileService cognitiveProfileService,
                IHostEnvironment hostEnvironment,
                CancellationToken cancellationToken) =>
            {
                var userId = ResolveUserId(httpContext);
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Results.Unauthorized();
                }

                var name = request.Name?.Trim() ?? string.Empty;
                if (name.Length == 0)
                {
                    return Results.BadRequest(new { error = "name is required." });
                }

                var now = DateTimeOffset.UtcNow;
                var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? Guid.NewGuid().ToString() : request.SessionId.Trim();
                var entity = new CompanionEntity
                {
                    CompanionId = Guid.NewGuid(),
                    UserId = userId,
                    Name = name,
                    Tone = string.IsNullOrWhiteSpace(request.Tone) ? "friendly" : request.Tone.Trim(),
                    Purpose = string.IsNullOrWhiteSpace(request.Purpose) ? "General companion" : request.Purpose.Trim(),
                    ModelHint = string.IsNullOrWhiteSpace(request.ModelHint) ? "openai:gpt-4.1-mini" : request.ModelHint.Trim(),
                    SessionId = sessionId,
                    OriginStory = request.OriginStory?.Trim() ?? string.Empty,
                    BirthDateUtc = request.BirthDateUtc,
                    InitialMemoryText = string.IsNullOrWhiteSpace(request.InitialMemoryText) ? null : request.InitialMemoryText.Trim(),
                    MetadataJson = BuildMetadataJson(request.MetadataJson, request.TemplateKey, request.SystemPrompt),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    IsArchived = false
                };

                dbContext.Companions.Add(entity);
                await dbContext.SaveChangesAsync(cancellationToken);

                var initialProfile = BuildInitialCognitiveProfile(request.CognitiveProfileJson);
                var createdVersion = await cognitiveProfileService.CreateVersionAsync(
                    new CreateCompanionCognitiveProfileVersionRequest(
                        entity.CompanionId,
                        userId,
                        initialProfile,
                        ChangeSummary: "Initial companion profile",
                        ChangeReason: "Companion creation bootstrap"),
                    cancellationToken);
                entity.ActiveCognitiveProfileVersionId = createdVersion.ProfileVersionId;
                await dbContext.SaveChangesAsync(cancellationToken);

                if (!hostEnvironment.IsEnvironment("Test"))
                {
                    await SeedCompanionMemoryBestEffortAsync(memoryToolsPlugin, entity);
                }

                return Results.Ok(ToDto(entity));
            });

        group.MapDelete(
            "/{companionId:guid}",
            async (HttpContext httpContext, Guid companionId, MemoryDbContext dbContext, CancellationToken cancellationToken) =>
            {
                var userId = ResolveUserId(httpContext);
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Results.Unauthorized();
                }

                var entity = await dbContext.Companions.FirstOrDefaultAsync(
                    x => x.CompanionId == companionId && x.UserId == userId,
                    cancellationToken);

                if (entity is null)
                {
                    return Results.NotFound();
                }

                entity.IsArchived = true;
                entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                return Results.Ok();
            });

        return endpoints;
    }

    private static string? ResolveUserId(HttpContext httpContext)
    {
        var claim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(claim) ? null : claim.Trim();
    }

    private static async Task SeedCompanionMemoryBestEffortAsync(MemoryToolsPlugin memoryToolsPlugin, CompanionEntity companion)
    {
        var seedEntries = new List<(string Hint, string Text)>
        {
            (
                "companion.profile",
                JsonSerializer.Serialize(
                    new
                    {
                        companion.Name,
                        companion.Tone,
                        companion.Purpose,
                        companion.ModelHint,
                        companion.UserId
                    })
            )
        };

        if (!string.IsNullOrWhiteSpace(companion.OriginStory))
        {
            seedEntries.Add(("companion.origin_story", companion.OriginStory));
        }

        if (companion.BirthDateUtc.HasValue)
        {
            seedEntries.Add(
                (
                    "companion.birth_datetime",
                    companion.BirthDateUtc.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
                ));
        }

        if (!string.IsNullOrWhiteSpace(companion.InitialMemoryText))
        {
            seedEntries.Add(("companion.initial_memory", companion.InitialMemoryText!));
        }

        var systemPrompt = TryReadMetadataField(companion.MetadataJson, "systemPrompt");
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            seedEntries.Add(("companion.system_prompt", systemPrompt!));
        }

        foreach (var seed in seedEntries)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await memoryToolsPlugin.StoreMemoryAsync(
                    companion.SessionId,
                    seed.Text,
                    seed.Hint,
                    timeout.Token);
            }
            catch
            {
                // Companion creation should not fail when optional memory seeding is unavailable.
            }
        }
    }

    private static CompanionDto ToDto(CompanionEntity entity)
        => new(
            entity.CompanionId,
            entity.UserId,
            entity.Name,
            entity.Tone,
            entity.Purpose,
            entity.ModelHint,
            entity.SessionId,
            entity.OriginStory,
            entity.BirthDateUtc,
            entity.InitialMemoryText,
            entity.ActiveCognitiveProfileVersionId,
            entity.MetadataJson,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            entity.IsArchived);

    private static CompanionCognitiveProfileDocument BuildInitialCognitiveProfile(string? cognitiveProfileJson)
    {
        if (string.IsNullOrWhiteSpace(cognitiveProfileJson))
        {
            return new CompanionCognitiveProfileDocument();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<CompanionCognitiveProfileDocument>(cognitiveProfileJson);
            return parsed ?? new CompanionCognitiveProfileDocument();
        }
        catch
        {
            return new CompanionCognitiveProfileDocument();
        }
    }

    private static string BuildMetadataJson(string? input, string? templateKey, string? systemPrompt)
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(input))
        {
            try
            {
                using var parsed = JsonDocument.Parse(input);
                if (parsed.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in parsed.RootElement.EnumerateObject())
                    {
                        map[property.Name] = property.Value.Clone();
                    }
                }
            }
            catch
            {
                // ignore invalid metadata and continue with explicit fields.
            }
        }

        if (!string.IsNullOrWhiteSpace(templateKey))
        {
            map["templateKey"] = templateKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            map["systemPrompt"] = systemPrompt.Trim();
        }

        if (map.Count == 0)
        {
            return "{}";
        }

        return JsonSerializer.Serialize(map);
    }

    private static string? TryReadMetadataField(string metadataJson, string key)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var parsed = JsonDocument.Parse(metadataJson);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!parsed.RootElement.TryGetProperty(key, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }
        catch
        {
            return null;
        }
    }
}

public sealed record CreateCompanionRequest(
    string Name,
    string? Tone = null,
    string? Purpose = null,
    string? ModelHint = null,
    string? SessionId = null,
    string? OriginStory = null,
    DateTimeOffset? BirthDateUtc = null,
    string? InitialMemoryText = null,
    string? CognitiveProfileJson = null,
    string? TemplateKey = null,
    string? SystemPrompt = null,
    string? MetadataJson = null);

public sealed record CompanionDto(
    Guid CompanionId,
    string UserId,
    string Name,
    string Tone,
    string Purpose,
    string ModelHint,
    string SessionId,
    string OriginStory,
    DateTimeOffset? BirthDateUtc,
    string? InitialMemoryText,
    Guid? ActiveCognitiveProfileVersionId,
    string MetadataJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool IsArchived);
