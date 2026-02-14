namespace CognitiveMemory.Application.Contracts;

public sealed class IngestDocumentResponse
{
    public Guid DocumentId { get; init; }

    public string Status { get; init; } = "Queued";

    public int ClaimsCreated { get; init; }
}
