using CognitiveMemory.Domain.Entities;

namespace CognitiveMemory.Application.Contracts;

public class ClaimListItem
{
    public Guid ClaimId { get; init; }

    public string Predicate { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public ClaimStatus Status { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ValidTo { get; init; }

    public int EvidenceCount { get; init; }
}
