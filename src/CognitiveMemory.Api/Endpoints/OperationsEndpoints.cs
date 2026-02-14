using CognitiveMemory.Application.AI.Tooling;

namespace CognitiveMemory.Api.Endpoints;

public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ops");

        group.MapGet("/outbox/summary", async (IOutboxRepository outboxRepository, CancellationToken cancellationToken) =>
        {
            var summary = await outboxRepository.GetStatusSummaryAsync(cancellationToken);
            return Results.Ok(summary);
        });

        group.MapGet("/outbox/events", async (int? take, string? status, IOutboxRepository outboxRepository, CancellationToken cancellationToken) =>
        {
            var normalizedStatus = NormalizeOutboxStatus(status);
            if (status is not null && normalizedStatus is null)
            {
                return Results.BadRequest(new
                {
                    message = "Invalid outbox status filter.",
                    allowed = new[] { OutboxStatuses.Pending, OutboxStatuses.Failed, OutboxStatuses.Processing, OutboxStatuses.Succeeded }
                });
            }

            var events = await outboxRepository.GetRecentAsync(take ?? 100, normalizedStatus, cancellationToken);
            return Results.Ok(new
            {
                count = events.Count,
                events
            });
        });

        group.MapGet("/calibration/summary", async (int? windowHours, int? reasonCodeSample, IClaimCalibrationRepository repository, CancellationToken cancellationToken) =>
        {
            var hours = Math.Clamp(windowHours ?? 24, 1, 720);
            var trend = await repository.GetTrendSummaryAsync(TimeSpan.FromHours(hours), cancellationToken);
            var sampleSize = Math.Clamp(reasonCodeSample ?? 200, 10, 500);
            var recent = await repository.GetRecentAsync(sampleSize, cancellationToken);

            var topReasonCodes = recent
                .SelectMany(r => JsonStringArrayCodec.DeserializeOrEmpty(r.ReasonCodesJson))
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Take(12)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);

            return Results.Ok(new
            {
                windowHours = hours,
                trend,
                topReasonCodes
            });
        });

        return app;
    }

    private static string? NormalizeOutboxStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        if (string.Equals(status, OutboxStatuses.Pending, StringComparison.OrdinalIgnoreCase))
        {
            return OutboxStatuses.Pending;
        }

        if (string.Equals(status, OutboxStatuses.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return OutboxStatuses.Failed;
        }

        if (string.Equals(status, OutboxStatuses.Processing, StringComparison.OrdinalIgnoreCase))
        {
            return OutboxStatuses.Processing;
        }

        if (string.Equals(status, OutboxStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
        {
            return OutboxStatuses.Succeeded;
        }

        return null;
    }
}
