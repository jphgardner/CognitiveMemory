using System.Text.Json;
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Planning;

public sealed class GoalPlanningService(
    IEpisodicMemoryRepository episodicRepository,
    IProceduralMemoryRepository proceduralRepository,
    GoalPlanningOptions options) : IGoalPlanningService
{
    public async Task<GoalPlanResult> GeneratePlanAsync(GenerateGoalPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId)) throw new ArgumentException("SessionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Goal)) throw new ArgumentException("Goal is required.", nameof(request));

        var sessionId = request.SessionId.Trim();
        var goal = request.Goal.Trim();
        var maxSteps = Math.Clamp(request.MaxSteps, 2, 20);
        var lookbackDays = Math.Max(1, request.LookbackDays ?? options.DefaultLookbackDays);

        var now = DateTimeOffset.UtcNow;
        var episodes = await episodicRepository.QueryRangeAsync(
            now.AddDays(-lookbackDays),
            now,
            Math.Clamp(options.MaxEpisodesScanned, 10, 2000),
            cancellationToken);

        var goalTerms = ExtractGoalTerms(goal);
        var relevantEpisodes = episodes
            .Where(x => ContainsAnyTerm(x.What, goalTerms))
            .OrderByDescending(x => x.OccurredAt)
            .Take(Math.Clamp(options.MaxSupportingSignals, 3, 50))
            .ToArray();

        var relatedRoutines = await LoadRelatedRoutinesAsync(goalTerms, cancellationToken);
        var steps = BuildPlanSteps(goal, relatedRoutines, relevantEpisodes, maxSteps);

        var plan = new GoalPlanResult(
            Guid.NewGuid(),
            sessionId,
            goal,
            steps,
            relevantEpisodes.Select(ToSignal).ToArray(),
            now);

        if (options.PersistPlansToEpisodicMemory)
        {
            var context = JsonSerializer.Serialize(
                new
                {
                    planId = plan.PlanId,
                    stepCount = plan.Steps.Count,
                    supportingSignals = plan.SupportingSignals.Count,
                    usedRoutineIds = relatedRoutines.Select(x => x.RoutineId).ToArray()
                });

            await episodicRepository.AppendAsync(
                new EpisodicMemoryEvent(
                    Guid.NewGuid(),
                    sessionId,
                    "planner",
                    $"Generated goal plan: {goal}",
                    now,
                    context,
                    "api:planning:generate"),
                cancellationToken);
        }

        return plan;
    }

    public async Task<RecordGoalOutcomeResult> RecordOutcomeAsync(
        RecordGoalOutcomeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PlanId == Guid.Empty) throw new ArgumentException("PlanId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SessionId)) throw new ArgumentException("SessionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Goal)) throw new ArgumentException("Goal is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OutcomeSummary)) throw new ArgumentException("OutcomeSummary is required.", nameof(request));

        var now = DateTimeOffset.UtcNow;
        var sessionId = request.SessionId.Trim();
        var goal = request.Goal.Trim();

        var context = JsonSerializer.Serialize(
            new
            {
                planId = request.PlanId,
                succeeded = request.Succeeded,
                executedSteps = request.ExecutedSteps,
                outcome = request.OutcomeSummary
            });

        await episodicRepository.AppendAsync(
            new EpisodicMemoryEvent(
                Guid.NewGuid(),
                sessionId,
                "planner",
                $"Plan outcome recorded: {goal}",
                now,
                context,
                "api:planning:outcome"),
            cancellationToken);

        Guid? routineId = null;
        var updatedProceduralMemory = false;

        if (request.Succeeded
            && options.AutoUpdateProceduralMemoryOnSuccess
            && request.ExecutedSteps.Count > 0)
        {
            var trigger = string.IsNullOrWhiteSpace(request.Trigger)
                ? InferTrigger(goal)
                : request.Trigger.Trim().ToLowerInvariant();

            var existing = await proceduralRepository.QueryByTriggerAsync(trigger, 1, cancellationToken);
            var existingRoutine = existing.FirstOrDefault();

            var mergedSteps = request.ExecutedSteps
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var routine = new ProceduralRoutine(
                existingRoutine?.RoutineId ?? Guid.NewGuid(),
                trigger,
                existingRoutine?.Name ?? $"Goal routine: {goal}",
                mergedSteps,
                ["Verify success criteria", "Persist updated guidance"],
                request.OutcomeSummary.Trim(),
                existingRoutine?.CreatedAtUtc ?? now,
                now);

            var saved = await proceduralRepository.UpsertAsync(routine, cancellationToken);
            routineId = saved.RoutineId;
            updatedProceduralMemory = true;
        }

        return new RecordGoalOutcomeResult(request.PlanId, routineId, updatedProceduralMemory, now);
    }

    private async Task<IReadOnlyList<ProceduralRoutine>> LoadRelatedRoutinesAsync(
        IReadOnlyList<string> goalTerms,
        CancellationToken cancellationToken)
    {
        var routines = new List<ProceduralRoutine>();
        var seen = new HashSet<Guid>();

        foreach (var term in goalTerms.Take(5))
        {
            var byTrigger = await proceduralRepository.QueryByTriggerAsync(term, 5, cancellationToken);
            foreach (var routine in byTrigger)
            {
                if (seen.Add(routine.RoutineId))
                {
                    routines.Add(routine);
                }
            }

            var searched = await proceduralRepository.SearchAsync(term, 5, cancellationToken);
            foreach (var routine in searched)
            {
                if (seen.Add(routine.RoutineId))
                {
                    routines.Add(routine);
                }
            }
        }

        return routines
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(10)
            .ToArray();
    }

    private static IReadOnlyList<GoalPlanStep> BuildPlanSteps(
        string goal,
        IReadOnlyList<ProceduralRoutine> routines,
        IReadOnlyList<EpisodicMemoryEvent> relevantEpisodes,
        int maxSteps)
    {
        var steps = new List<GoalPlanStep>();

        if (routines.Count > 0)
        {
            var primary = routines[0];
            foreach (var step in primary.Steps.Take(maxSteps))
            {
                steps.Add(new GoalPlanStep(steps.Count + 1, step, $"procedural:{primary.RoutineId}"));
            }
        }

        if (steps.Count == 0)
        {
            var defaults = new[]
            {
                $"Define success criteria for goal: {goal}.",
                "Retrieve and review the most relevant past episodes and claims.",
                "Break the goal into concrete milestones with clear validation points.",
                "Execute the first milestone and capture outcome signals.",
                "Revise procedure based on outcome and persist learning."
            };

            foreach (var step in defaults.Take(maxSteps))
            {
                steps.Add(new GoalPlanStep(steps.Count + 1, step, "generated"));
            }
        }

        foreach (var episode in relevantEpisodes.Take(2))
        {
            if (steps.Count >= maxSteps)
            {
                break;
            }

            steps.Add(
                new GoalPlanStep(
                    steps.Count + 1,
                    $"Incorporate historical signal: {TrimForDisplay(episode.What)}",
                    $"episodic:{episode.EventId}"));
        }

        return steps.Take(maxSteps).ToArray();
    }

    private static IReadOnlyList<string> ExtractGoalTerms(string goal)
    {
        var cleaned = new string(
            goal
                .ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                .ToArray());

        var stopWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "the", "and", "for", "with", "this", "that", "from", "into", "about", "goal", "plan", "build", "make", "create"
        };

        return cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 3)
            .Where(x => !stopWords.Contains(x))
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();
    }

    private static bool ContainsAnyTerm(string text, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return false;
        }

        var normalized = text.ToLowerInvariant();
        return terms.Any(term => normalized.Contains(term, StringComparison.Ordinal));
    }

    private static string ToSignal(EpisodicMemoryEvent x) =>
        $"{x.OccurredAt:yyyy-MM-dd HH:mm} | {TrimForDisplay(x.What)}";

    private static string TrimForDisplay(string value)
    {
        if (value.Length <= 110)
        {
            return value;
        }

        return value[..110];
    }

    private static string InferTrigger(string goal)
    {
        var tokens = ExtractGoalTerms(goal);
        if (tokens.Count == 0)
        {
            return "goal-workflow";
        }

        return string.Join(' ', tokens.Take(4));
    }
}
