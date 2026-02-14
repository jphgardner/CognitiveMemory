using CognitiveMemory.Domain.Entities;

namespace CognitiveMemory.Application.Contracts;

public class ClaimCreatedResponse
{
    public Guid ClaimId { get; init; }

    public Guid SubjectEntityId { get; init; }

    public string Predicate { get; init; } = string.Empty;

    public string? LiteralValue { get; init; }

    public Guid? ObjectEntityId { get; init; }

    public string ValueType { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public ClaimStatus Status { get; init; }

    public string Scope { get; init; } = "{}";

    public DateTimeOffset CreatedAt { get; init; }
}
