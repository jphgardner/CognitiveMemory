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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CognitiveMemory.Api.Tests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IHostedService));
            services.RemoveAll(typeof(DbContextOptions<MemoryDbContext>));
            services.RemoveAll(typeof(MemoryDbContext));

            services.AddDbContext<MemoryDbContext>(options => options.UseInMemoryDatabase("cognitive-memory-tests"));

            services.RemoveAll(typeof(IChatService));
            services.RemoveAll(typeof(IEpisodicMemoryService));
            services.RemoveAll(typeof(ISemanticMemoryService));
            services.RemoveAll(typeof(IConsolidationService));
            services.RemoveAll(typeof(ICognitiveReasoningService));
            services.RemoveAll(typeof(IGoalPlanningService));
            services.RemoveAll(typeof(IIdentityEvolutionService));
            services.RemoveAll(typeof(ITruthMaintenanceService));

            services.AddSingleton<IChatService>(new StubChatService());
            services.AddSingleton<IEpisodicMemoryService>(new StubEpisodicService());
            services.AddSingleton<ISemanticMemoryService>(new StubSemanticService());
            services.AddSingleton<IConsolidationService>(new StubConsolidationService());
            services.AddSingleton<ICognitiveReasoningService>(new StubReasoningService());
            services.AddSingleton<IGoalPlanningService>(new StubPlanningService());
            services.AddSingleton<IIdentityEvolutionService>(new StubIdentityService());
            services.AddSingleton<ITruthMaintenanceService>(new StubTruthService());
        });
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
}
