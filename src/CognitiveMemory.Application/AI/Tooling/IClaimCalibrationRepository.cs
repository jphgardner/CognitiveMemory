namespace CognitiveMemory.Application.AI.Tooling;

public sealed class ClaimCalibrationRecord
{
    public Guid ClaimId { get; init; }

    public double RecommendedConfidence { get; init; }

    public string SourceEventRef { get; init; } = string.Empty;

    public string ReasonCodesJson { get; init; } = "[]";

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class CalibrationTrendSummary
{
    public int WindowCount { get; init; }

    public int DistinctClaims { get; init; }

    public double AverageRecommendedConfidence { get; init; }

    public double MinRecommendedConfidence { get; init; }

    public double MaxRecommendedConfidence { get; init; }
}

public interface IClaimCalibrationRepository
{
    Task AddAsync(ClaimCalibrationRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, ClaimCalibrationRecord>> GetLatestByClaimIdsAsync(IReadOnlyCollection<Guid> claimIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClaimCalibrationRecord>> GetRecentAsync(int take, CancellationToken cancellationToken);

    Task<CalibrationTrendSummary> GetTrendSummaryAsync(TimeSpan window, CancellationToken cancellationToken);
}
