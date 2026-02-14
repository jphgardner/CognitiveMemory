namespace CognitiveMemory.Application.AI.Tooling;

public sealed class ClaimInsightRecord
{
    public Guid ClaimId { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public string SourceEventRef { get; init; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; init; }
}

public interface IClaimInsightRepository
{
    Task UpsertAsync(ClaimInsightRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, ClaimInsightRecord>> GetByClaimIdsAsync(IReadOnlyCollection<Guid> claimIds, CancellationToken cancellationToken);
}
