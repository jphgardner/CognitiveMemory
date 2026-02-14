namespace CognitiveMemory.Api.Configuration;

public enum ChatPersistenceMode
{
    AgentOnly = 0,
    HybridFallback = 1,
    SystemPostTurn = 2
}

public sealed class ChatPersistenceOptions
{
    public const string SectionName = "ChatPersistence";

    public ChatPersistenceMode Mode { get; init; } = ChatPersistenceMode.AgentOnly;

    public bool IngestAssistantTurns { get; init; } = false;
}
