using System.Text.Json;
using CognitiveMemory.Application.AI;

namespace CognitiveMemory.Application.Tests;

public class PromptCatalogTests
{
    [Fact]
    public void PromptAssetsExistAndSchemaIsValidJson()
    {
        var repoRoot = GetRepositoryRoot();
        var promptPaths = new[]
        {
            PromptCatalog.ClaimExtractionPromptPath,
            PromptCatalog.DebateGeneratorPromptPath,
            PromptCatalog.DebateSkepticPromptPath,
            PromptCatalog.DebateLibrarianPromptPath,
            PromptCatalog.DebateJudgePromptPath,
            PromptCatalog.ConscienceAnalyzerPromptPath,
            PromptCatalog.ChatAgentSystemPromptPath
        };

        foreach (var prompt in promptPaths)
        {
            var promptPath = Path.Combine(repoRoot, prompt);
            Assert.True(File.Exists(promptPath), $"Prompt file not found: {promptPath}");
        }

        var schemaPath = Path.Combine(repoRoot, PromptCatalog.ClaimExtractionSchemaPath);

        Assert.True(File.Exists(schemaPath), $"Schema file not found: {schemaPath}");

        var schemaContent = File.ReadAllText(schemaPath);
        using var document = JsonDocument.Parse(schemaContent);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CognitiveMemory.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
