namespace CognitiveMemory.Application.Chat;

public sealed record ChatRequest(string Message, string? SessionId = null);
