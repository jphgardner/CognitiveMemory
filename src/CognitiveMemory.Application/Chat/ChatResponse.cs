namespace CognitiveMemory.Application.Chat;

public sealed record ChatResponse(
    string SessionId,
    string Answer,
    DateTimeOffset GeneratedAtUtc,
    int ContextTurnCount);
