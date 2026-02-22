using CognitiveMemory.Application.Chat;
using CognitiveMemory.Application.Consolidation;
using CognitiveMemory.Application.Episodic;
using CognitiveMemory.Application.Identity;
using CognitiveMemory.Application.Planning;
using CognitiveMemory.Application.Reasoning;
using CognitiveMemory.Application.Semantic;
using CognitiveMemory.Application.Truth;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using CognitiveMemory.Infrastructure.Subconscious;
using CognitiveMemory.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CognitiveMemory.Api.Tests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IHostedService));
            RemoveMemoryDbRegistrations(services);

            services.AddDbContext<MemoryDbContext>(options => options.UseInMemoryDatabase("cognitive-memory-tests"));

            services.RemoveAll(typeof(IChatService));
            services.RemoveAll(typeof(IEpisodicMemoryService));
            services.RemoveAll(typeof(ISemanticMemoryService));
            services.RemoveAll(typeof(IConsolidationService));
            services.RemoveAll(typeof(ICognitiveReasoningService));
            services.RemoveAll(typeof(IGoalPlanningService));
            services.RemoveAll(typeof(IIdentityEvolutionService));
            services.RemoveAll(typeof(ITruthMaintenanceService));
            services.RemoveAll(typeof(ISubconsciousDebateService));
            services.RemoveAll(typeof(IWorkingMemoryStore));

            services.AddSingleton<IChatService>(new StubChatService());
            services.AddSingleton<IEpisodicMemoryService>(new StubEpisodicService());
            services.AddSingleton<ISemanticMemoryService>(new StubSemanticService());
            services.AddSingleton<IConsolidationService>(new StubConsolidationService());
            services.AddSingleton<ICognitiveReasoningService>(new StubReasoningService());
            services.AddSingleton<IGoalPlanningService>(new StubPlanningService());
            services.AddSingleton<IIdentityEvolutionService>(new StubIdentityService());
            services.AddSingleton<ITruthMaintenanceService>(new StubTruthService());
            services.AddScoped<ISubconsciousDebateService, StubSubconsciousDebateService>();
            services.AddSingleton<IWorkingMemoryStore>(new StubWorkingMemoryStore());
        });
    }

    private static void RemoveMemoryDbRegistrations(IServiceCollection services)
    {
        services.RemoveAll(typeof(DbContextOptions<MemoryDbContext>));
        services.RemoveAll(typeof(MemoryDbContext));

        var descriptors = services
            .Where(
                d =>
                {
                    var type = d.ServiceType;
                    if (type == typeof(MemoryDbContext))
                    {
                        return true;
                    }

                    if (!type.IsGenericType)
                    {
                        return false;
                    }

                    var definition = type.GetGenericTypeDefinition();
                    var args = type.GetGenericArguments();
                    return args.Length == 1
                           && args[0] == typeof(MemoryDbContext)
                           && (definition == typeof(IDbContextFactory<>)
                               || definition == typeof(DbContextOptions<>)
                               || definition == typeof(IDbContextOptionsConfiguration<>)
                               || definition == typeof(IConfigureOptions<>)
                               || definition == typeof(IPostConfigureOptions<>));
                })
            .ToArray();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }

        // Remove Aspire-specific option registrations that can enforce external connection-string validation in tests.
        var aspireOptionDescriptors = services
            .Where(
                d =>
                {
                    var type = d.ImplementationType;
                    if (type is null)
                    {
                        return false;
                    }

                    if (!type.FullName!.Contains("Aspire", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (!d.ServiceType.IsGenericType)
                    {
                        return false;
                    }

                    var definition = d.ServiceType.GetGenericTypeDefinition();
                    return definition == typeof(IConfigureOptions<>) || definition == typeof(IPostConfigureOptions<>);
                })
            .ToArray();

        foreach (var descriptor in aspireOptionDescriptors)
        {
            services.Remove(descriptor);
        }
    }

    private sealed class StubChatService : IChatService
    {
        public Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse("test-session", "ok", DateTimeOffset.UtcNow, 2));

        public async IAsyncEnumerable<ChatStreamChunk> AskStreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatStreamChunk("test-session", "o", false, DateTimeOffset.UtcNow, 1);
            yield return new ChatStreamChunk("test-session", "k", false, DateTimeOffset.UtcNow, 1);
            await Task.Yield();
            yield return new ChatStreamChunk("test-session", string.Empty, true, DateTimeOffset.UtcNow, 2);
        }
    }

    private sealed class StubEpisodicService : IEpisodicMemoryService
    {
        public Task<EpisodicMemoryEvent> AppendAsync(AppendEpisodicMemoryRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new EpisodicMemoryEvent(Guid.NewGuid(), request.SessionId, request.Who, request.What, DateTimeOffset.UtcNow, request.Context, request.SourceReference));

        public Task<IReadOnlyList<EpisodicMemoryEvent>> QueryBySessionAsync(string sessionId, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EpisodicMemoryEvent>>([]);
    }

    private sealed class StubSemanticService : ISemanticMemoryService
    {
        public Task<SemanticClaim> CreateClaimAsync(CreateSemanticClaimRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new SemanticClaim(Guid.NewGuid(), request.Subject, request.Predicate, request.Value, request.Confidence, request.Scope, request.Status, request.ValidFromUtc, request.ValidToUtc, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        public Task<ClaimEvidence> AddEvidenceAsync(AddClaimEvidenceRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ClaimEvidence(Guid.NewGuid(), request.ClaimId, request.SourceType, request.SourceReference, request.ExcerptOrSummary, request.Strength, DateTimeOffset.UtcNow));

        public Task<ClaimContradiction> AddContradictionAsync(AddClaimContradictionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ClaimContradiction(Guid.NewGuid(), request.ClaimAId, request.ClaimBId, request.Type, request.Severity, DateTimeOffset.UtcNow, request.Status));

        public Task<IReadOnlyList<SemanticClaim>> QueryClaimsAsync(string? subject = null, string? predicate = null, SemanticClaimStatus? status = null, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SemanticClaim>>([]);

        public Task<SemanticClaim> SupersedeClaimAsync(SupersedeSemanticClaimRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new SemanticClaim(Guid.NewGuid(), request.Subject, request.Predicate, request.Value, request.Confidence, request.Scope, SemanticClaimStatus.Active, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        public Task<int> RunDecayAsync(int staleDays, double decayStep, double minConfidence, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class StubConsolidationService : IConsolidationService
    {
        public Task<ConsolidationRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ConsolidationRunResult(1, 1, 1, 0, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow));
    }

    private sealed class StubReasoningService : ICognitiveReasoningService
    {
        public Task<CognitiveReasoningRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CognitiveReasoningRunResult(10, 8, 2, 1, 1, 1, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow));
    }

    private sealed class StubPlanningService : IGoalPlanningService
    {
        public Task<GoalPlanResult> GeneratePlanAsync(GenerateGoalPlanRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(
                new GoalPlanResult(
                    Guid.NewGuid(),
                    request.SessionId,
                    request.Goal,
                    [new GoalPlanStep(1, "Do thing", "generated")],
                    ["signal"],
                    DateTimeOffset.UtcNow));

        public Task<RecordGoalOutcomeResult> RecordOutcomeAsync(RecordGoalOutcomeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new RecordGoalOutcomeResult(request.PlanId, Guid.NewGuid(), true, DateTimeOffset.UtcNow));
    }

    private sealed class StubIdentityService : IIdentityEvolutionService
    {
        public Task<IdentityEvolutionRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new IdentityEvolutionRunResult(10, 5, 2, 1, ["identity.project_focus"], DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow));
    }

    private sealed class StubTruthService : ITruthMaintenanceService
    {
        public Task<TruthMaintenanceRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new TruthMaintenanceRunResult(12, 2, 1, 1, 1, ["clarify"], DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow));
    }

    private sealed class StubSubconsciousDebateService(IServiceScopeFactory scopeFactory) : ISubconsciousDebateService
    {
        public async Task QueueDebateAsync(string sessionId, SubconsciousDebateTopic topic, CancellationToken cancellationToken = default)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MemoryDbContext>();
            var companionId = await db.Companions
                .Where(x => x.SessionId == sessionId)
                .Select(x => (Guid?)x.CompanionId)
                .FirstOrDefaultAsync(cancellationToken) ?? Guid.Empty;
            var now = DateTimeOffset.UtcNow;
            db.SubconsciousDebateSessions.Add(
                new SubconsciousDebateSessionEntity
                {
                    DebateId = Guid.NewGuid(),
                    CompanionId = companionId,
                    SessionId = sessionId,
                    TopicKey = topic.TopicKey,
                    TriggerEventId = topic.TriggerEventId,
                    TriggerEventType = topic.TriggerEventType,
                    TriggerPayloadJson = topic.TriggerPayloadJson,
                    State = "Queued",
                    Priority = 50,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            await db.SaveChangesAsync(cancellationToken);
        }

        public Task ProcessDebateAsync(Guid debateId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<bool> ApproveDebateAsync(Guid debateId, CancellationToken cancellationToken = default)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MemoryDbContext>();
            var row = await db.SubconsciousDebateSessions.FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
            if (row is null)
            {
                return false;
            }

            row.State = "Completed";
            row.CompletedAtUtc = DateTimeOffset.UtcNow;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<bool> RejectDebateAsync(Guid debateId, CancellationToken cancellationToken = default)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MemoryDbContext>();
            var row = await db.SubconsciousDebateSessions.FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
            if (row is null)
            {
                return false;
            }

            row.State = "Completed";
            row.CompletedAtUtc = DateTimeOffset.UtcNow;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
    }

    private sealed class StubWorkingMemoryStore : IWorkingMemoryStore
    {
        private readonly Dictionary<string, WorkingMemoryContext> contexts = new(StringComparer.Ordinal);

        public Task<WorkingMemoryContext> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            if (contexts.TryGetValue(sessionId, out var existing))
            {
                return Task.FromResult(existing);
            }

            return Task.FromResult(new WorkingMemoryContext(sessionId, []));
        }

        public Task SaveAsync(WorkingMemoryContext context, CancellationToken cancellationToken = default)
        {
            contexts[context.SessionId] = context;
            return Task.CompletedTask;
        }
    }
}
