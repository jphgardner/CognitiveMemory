using System.Text.Json;
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Scheduling;
using CognitiveMemory.Infrastructure.Subconscious;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Background;

public sealed class ScheduledActionWorker(
    IServiceScopeFactory scopeFactory,
    ScheduledActionOptions options,
    ILogger<ScheduledActionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Scheduled action worker disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        logger.LogInformation("Scheduled action worker started. Poll={Poll}s Batch={Batch}", options.PollIntervalSeconds, options.BatchSize);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled action worker cycle failed.");
            }
        }
    }

    private async Task ProcessCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IScheduledActionStore>();
        var due = await store.ClaimDueAsync(DateTimeOffset.UtcNow, options.BatchSize, cancellationToken);
        if (due.Count == 0)
        {
            return;
        }

        foreach (var action in due)
        {
            try
            {
                await ExecuteActionAsync(scope.ServiceProvider, action, cancellationToken);
                await store.MarkCompletedAsync(action.ActionId, cancellationToken);
            }
            catch (Exception ex)
            {
                var exhausted = action.Attempts >= action.MaxAttempts;
                await store.MarkFailedAsync(action.ActionId, ex.Message, exhausted, cancellationToken);
                logger.LogWarning(
                    ex,
                    "Scheduled action execution failed. ActionId={ActionId} Type={Type} Attempt={Attempt}/{Max}",
                    action.ActionId,
                    action.ActionType,
                    action.Attempts,
                    action.MaxAttempts);
            }
        }
    }

    private static async Task ExecuteActionAsync(IServiceProvider services, Persistence.Entities.ScheduledActionEntity action, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(action.InputJson);
        var root = json.RootElement;
        var normalizedType = action.ActionType.Trim().ToLowerInvariant();

        switch (normalizedType)
        {
            case "append_episodic":
            {
                var episodic = services.GetRequiredService<IEpisodicMemoryRepository>();
                var who = root.TryGetProperty("who", out var whoProp) ? whoProp.GetString() ?? "system" : "system";
                var what = root.TryGetProperty("what", out var whatProp) ? whatProp.GetString() ?? "Scheduled action triggered." : "Scheduled action triggered.";
                var context = root.TryGetProperty("context", out var ctxProp) ? ctxProp.GetString() ?? "scheduled-action" : "scheduled-action";
                var sourceReference = root.TryGetProperty("sourceReference", out var srcProp) ? srcProp.GetString() ?? "scheduled-action" : "scheduled-action";
                await episodic.AppendAsync(
                    new EpisodicMemoryEvent(
                        Guid.NewGuid(),
                        action.SessionId,
                        who,
                        what,
                        DateTimeOffset.UtcNow,
                        context,
                        sourceReference),
                    cancellationToken);
                break;
            }
            case "queue_subconscious_debate":
            {
                var debates = services.GetRequiredService<ISubconsciousDebateService>();
                var topicKey = root.TryGetProperty("topicKey", out var topicProp) ? topicProp.GetString() ?? "scheduled-topic" : "scheduled-topic";
                var triggerEventType = root.TryGetProperty("triggerEventType", out var evtProp) ? evtProp.GetString() ?? "ScheduledActionTriggered" : "ScheduledActionTriggered";
                var triggerPayloadJson = root.TryGetProperty("triggerPayloadJson", out var payloadProp) && payloadProp.ValueKind == JsonValueKind.String
                    ? payloadProp.GetString() ?? "{}"
                    : action.InputJson;
                await debates.QueueDebateAsync(
                    action.SessionId,
                    new SubconsciousDebateTopic(topicKey, triggerEventType, null, triggerPayloadJson),
                    cancellationToken);
                break;
            }
            case "store_memory":
            {
                await ExecuteStoreMemoryAsync(services, action, root, cancellationToken);
                break;
            }
            case "execute_procedural_trigger":
            {
                var procedural = services.GetRequiredService<IProceduralMemoryRepository>();
                var episodic = services.GetRequiredService<IEpisodicMemoryRepository>();
                var trigger = RequireString(root, "trigger", "execute_procedural_trigger requires input.trigger.");
                var routines = await procedural.QueryByTriggerAsync(trigger, take: 1, cancellationToken);
                var selected = routines.FirstOrDefault();
                if (selected is null)
                {
                    throw new InvalidOperationException($"No procedural routine found for trigger '{trigger}'.");
                }

                var who = root.TryGetProperty("who", out var whoProp) ? whoProp.GetString() ?? "system" : "system";
                var context = root.TryGetProperty("context", out var ctxProp) ? ctxProp.GetString() ?? "scheduled-procedural" : "scheduled-procedural";
                var sourceReference = root.TryGetProperty("sourceReference", out var srcProp) ? srcProp.GetString() ?? "scheduled-action:execute_procedural_trigger" : "scheduled-action:execute_procedural_trigger";
                var what = $"Executed procedural routine '{selected.Name}' for trigger '{selected.Trigger}'. Steps: {string.Join(" | ", selected.Steps)}";
                await episodic.AppendAsync(
                    new EpisodicMemoryEvent(
                        Guid.NewGuid(),
                        action.SessionId,
                        who,
                        what,
                        DateTimeOffset.UtcNow,
                        context,
                        sourceReference),
                    cancellationToken);
                break;
            }
            case "invoke_webhook":
            {
                var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
                var methodRaw = root.TryGetProperty("method", out var methodProp) ? methodProp.GetString() ?? "POST" : "POST";
                var method = new HttpMethod(methodRaw.ToUpperInvariant());
                var urlRaw = RequireString(root, "url", "invoke_webhook requires input.url.");
                if (!Uri.TryCreate(urlRaw, UriKind.Absolute, out var uri))
                {
                    throw new InvalidOperationException("invoke_webhook input.url must be absolute.");
                }

                var client = httpClientFactory.CreateClient();
                if (root.TryGetProperty("timeoutSeconds", out var timeoutProp) && timeoutProp.TryGetInt32(out var timeoutSeconds))
                {
                    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 2, 120));
                }

                using var request = new HttpRequestMessage(method, uri);
                if (root.TryGetProperty("headers", out var headersProp) && headersProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var header in headersProp.EnumerateObject())
                    {
                        var value = header.Value.GetString();
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }

                        if (!request.Headers.TryAddWithoutValidation(header.Name, value))
                        {
                            request.Content ??= new StringContent(string.Empty);
                            _ = request.Content.Headers.TryAddWithoutValidation(header.Name, value);
                        }
                    }
                }

                if (root.TryGetProperty("body", out var bodyProp))
                {
                    var contentType = root.TryGetProperty("contentType", out var contentTypeProp)
                        ? contentTypeProp.GetString() ?? "application/json"
                        : "application/json";
                    var bodyText = bodyProp.ValueKind == JsonValueKind.String
                        ? bodyProp.GetString() ?? string.Empty
                        : bodyProp.GetRawText();
                    request.Content = new StringContent(bodyText, System.Text.Encoding.UTF8, contentType);
                }

                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new InvalidOperationException($"Webhook failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body={Truncate(detail, 800)}");
                }

                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported scheduled action type: {action.ActionType}");
        }
    }

    private static async Task ExecuteStoreMemoryAsync(
        IServiceProvider services,
        Persistence.Entities.ScheduledActionEntity action,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var layer = root.TryGetProperty("layer", out var layerProp) ? layerProp.GetString() ?? "semantic" : "semantic";
        var normalizedLayer = layer.Trim().ToLowerInvariant();
        var memoryText = root.TryGetProperty("memoryText", out var memoryTextProp) ? memoryTextProp.GetString() : null;

        switch (normalizedLayer)
        {
            case "working":
            {
                var store = services.GetRequiredService<IWorkingMemoryStore>();
                var context = await store.GetAsync(action.SessionId, cancellationToken);
                var role = root.TryGetProperty("role", out var roleProp) ? roleProp.GetString() ?? "assistant" : "assistant";
                var content = !string.IsNullOrWhiteSpace(memoryText) ? memoryText : RequireString(root, "value", "working layer requires memoryText or value.");
                var turns = context.Turns
                    .Concat([new WorkingMemoryTurn(role, content, DateTimeOffset.UtcNow)])
                    .TakeLast(20)
                    .ToArray();
                await store.SaveAsync(new WorkingMemoryContext(action.SessionId, turns), cancellationToken);
                break;
            }
            case "episodic":
            {
                var episodic = services.GetRequiredService<IEpisodicMemoryRepository>();
                var who = root.TryGetProperty("who", out var whoProp) ? whoProp.GetString() ?? "system" : "system";
                var what = !string.IsNullOrWhiteSpace(memoryText) ? memoryText : RequireString(root, "what", "episodic layer requires memoryText or what.");
                var context = root.TryGetProperty("context", out var ctxProp) ? ctxProp.GetString() ?? "scheduled-store-memory" : "scheduled-store-memory";
                var sourceReference = root.TryGetProperty("sourceReference", out var srcProp)
                    ? srcProp.GetString() ?? "scheduled-action:store_memory"
                    : "scheduled-action:store_memory";
                await episodic.AppendAsync(
                    new EpisodicMemoryEvent(
                        Guid.NewGuid(),
                        action.SessionId,
                        who,
                        what,
                        DateTimeOffset.UtcNow,
                        context,
                        sourceReference),
                    cancellationToken);
                break;
            }
            case "semantic":
            {
                var semantic = services.GetRequiredService<ISemanticMemoryRepository>();
                var subject = root.TryGetProperty("subject", out var subjectProp) ? subjectProp.GetString() ?? $"session:{action.SessionId}" : $"session:{action.SessionId}";
                var predicate = root.TryGetProperty("predicate", out var predicateProp) ? predicateProp.GetString() ?? "states" : "states";
                var value = !string.IsNullOrWhiteSpace(memoryText) ? memoryText : RequireString(root, "value", "semantic layer requires memoryText or value.");
                var confidence = root.TryGetProperty("confidence", out var confProp) && confProp.TryGetDouble(out var parsedConfidence)
                    ? Math.Clamp(parsedConfidence, 0, 1)
                    : 0.72;
                var scope = root.TryGetProperty("scope", out var scopeProp) ? scopeProp.GetString() ?? "global" : "global";
                var now = DateTimeOffset.UtcNow;
                await semantic.CreateClaimAsync(
                    new SemanticClaim(
                        Guid.NewGuid(),
                        subject,
                        predicate,
                        value,
                        confidence,
                        scope,
                        SemanticClaimStatus.Active,
                        null,
                        null,
                        null,
                        now,
                        now),
                    cancellationToken);
                break;
            }
            case "procedural":
            {
                var procedural = services.GetRequiredService<IProceduralMemoryRepository>();
                var trigger = RequireString(root, "trigger", "procedural layer requires trigger.");
                var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "scheduled routine" : "scheduled routine";
                var outcome = !string.IsNullOrWhiteSpace(memoryText) ? memoryText : (root.TryGetProperty("outcome", out var outProp) ? outProp.GetString() ?? string.Empty : string.Empty);
                if (string.IsNullOrWhiteSpace(outcome))
                {
                    outcome = "Scheduled procedural routine stored.";
                }

                var steps = root.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == JsonValueKind.Array
                    ? stepsProp.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray()
                    : ["Review context", "Execute action", "Record result"];
                var checkpoints = root.TryGetProperty("checkpoints", out var checkpointsProp) && checkpointsProp.ValueKind == JsonValueKind.Array
                    ? checkpointsProp.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray()
                    : Array.Empty<string>();
                var now = DateTimeOffset.UtcNow;
                await procedural.UpsertAsync(
                    new ProceduralRoutine(
                        Guid.NewGuid(),
                        trigger,
                        name,
                        steps,
                        checkpoints,
                        outcome,
                        now,
                        now),
                    cancellationToken);
                break;
            }
            case "self":
            {
                var self = services.GetRequiredService<ISelfModelRepository>();
                var key = RequireString(root, "key", "self layer requires key.");
                var value = !string.IsNullOrWhiteSpace(memoryText) ? memoryText : RequireString(root, "value", "self layer requires memoryText or value.");
                await self.SetPreferenceAsync(key, value, cancellationToken);
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported layer for store_memory action: {layer}");
        }
    }

    private static string RequireString(JsonElement root, string propertyName, string error)
    {
        if (root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()))
        {
            return value.GetString()!.Trim();
        }

        throw new InvalidOperationException(error);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
