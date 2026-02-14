using CognitiveMemory.Domain.Entities;

namespace CognitiveMemory.Application.Contracts;

public sealed class ClaimLifecycleResponse
{
    public Guid ClaimId { get; init; }

    public ClaimStatus Status { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
