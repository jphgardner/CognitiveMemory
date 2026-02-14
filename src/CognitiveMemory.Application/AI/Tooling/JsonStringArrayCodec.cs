using System.Text.Json;

namespace CognitiveMemory.Application.AI.Tooling;

public static class JsonStringArrayCodec
{
    public static string Serialize(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return "[]";
        }

        return JsonSerializer.Serialize(values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal));
    }

    public static IReadOnlyList<string> DeserializeOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json)?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
