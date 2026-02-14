using System.Collections.Concurrent;

namespace CognitiveMemory.Application.AI;

public static class PromptLoader
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string LoadText(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return Cache.GetOrAdd(relativePath, static path =>
        {
            var resolvedPath = ResolvePath(path);
            if (resolvedPath is null)
            {
                throw new FileNotFoundException(
                    $"Prompt file '{path}' was not found. Ensure prompts are present in the repository 'prompts/' folder.",
                    path);
            }

            return File.ReadAllText(resolvedPath);
        });
    }

    public static Task<string> LoadTextAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LoadText(relativePath));
    }

    public static string RenderTemplate(string template, IReadOnlyDictionary<string, string?> values)
    {
        var rendered = template;
        foreach (var (key, value) in values)
        {
            rendered = rendered.Replace($"{{{{{key}}}}}", value ?? string.Empty, StringComparison.Ordinal);
        }

        return rendered;
    }

    private static string? ResolvePath(string relativePath)
    {
        foreach (var start in EnumerateStartDirectories())
        {
            var resolved = WalkUpForFile(start, relativePath);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateStartDirectories()
    {
        if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
        {
            yield return AppContext.BaseDirectory;
        }

        var current = Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;
        }
    }

    private static string? WalkUpForFile(string startDirectory, string relativePath)
    {
        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(startDirectory);
        }
        catch
        {
            return null;
        }

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
