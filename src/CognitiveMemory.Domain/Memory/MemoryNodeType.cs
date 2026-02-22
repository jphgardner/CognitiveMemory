namespace CognitiveMemory.Domain.Memory;

public enum MemoryNodeType
{
    SemanticClaim = 0,
    EpisodicEvent = 1,
    ProceduralRoutine = 2,
    SelfPreference = 3,
    ScheduledAction = 4,
    SubconsciousDebate = 5,
    ToolInvocation = 6
}
