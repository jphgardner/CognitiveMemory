namespace CognitiveMemory.Infrastructure.Services;

public sealed class QueryCacheOptions
{
    public const string SectionName = "QueryCache";

    public int TtlSeconds { get; init; } = 120;
}
