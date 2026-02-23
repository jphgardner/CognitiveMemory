namespace CognitiveMemory.Infrastructure.Persistence;

public static class SqlLikePattern
{
    public static string Contains(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var escaped = normalized
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}
