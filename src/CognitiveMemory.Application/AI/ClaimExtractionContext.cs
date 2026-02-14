using System.Collections.Generic;

namespace CognitiveMemory.Application.AI;

public sealed class ClaimExtractionContext
{
    public string SourceType { get; init; } = "Other";

    public string SourceRef { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
