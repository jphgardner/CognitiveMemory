namespace CognitiveMemory.Application.Episodic;

public sealed record AppendEpisodicMemoryRequest(
    string SessionId,
    string Who,
    string What,
    string Context,
    string SourceReference,
    DateTimeOffset? OccurredAtUtc = null);
