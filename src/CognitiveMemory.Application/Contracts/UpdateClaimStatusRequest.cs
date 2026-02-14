namespace CognitiveMemory.Application.Contracts;

public sealed class UpdateClaimStatusRequest
{
    public Guid? ReplacementClaimId { get; set; }
}
