namespace CognitiveMemory.Application.Chat;

public sealed record ChatStreamChunk(
    string SessionId,
    string Delta,
    bool IsFinal,
    DateTimeOffset GeneratedAtUtc,
    int ContextTurnCount);
