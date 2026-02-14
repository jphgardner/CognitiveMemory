namespace CognitiveMemory.Application.AI;

public static class PromptCatalog
{
    public const string ClaimExtraction = "claim-extraction-v1";
    public const string DebateGenerator = "debate-generator-v1";
    public const string DebateSkeptic = "debate-skeptic-v1";
    public const string DebateLibrarian = "debate-librarian-v1";
    public const string DebateJudge = "debate-judge-v1";
    public const string ConscienceAnalyzer = "conscience-analyzer-v1";

    public static readonly string ClaimExtractionPromptPath = Path.Combine("prompts", "claim-extraction", "prompt.md");

    public static readonly string ClaimExtractionSchemaPath = Path.Combine("prompts", "claim-extraction", "schema.json");

    public static readonly string DebateGeneratorPromptPath = Path.Combine("prompts", "debate", "generator", "prompt.md");
    public static readonly string DebateSkepticPromptPath = Path.Combine("prompts", "debate", "skeptic", "prompt.md");
    public static readonly string DebateLibrarianPromptPath = Path.Combine("prompts", "debate", "librarian", "prompt.md");
    public static readonly string DebateJudgePromptPath = Path.Combine("prompts", "debate", "judge", "prompt.md");
    public static readonly string ConscienceAnalyzerPromptPath = Path.Combine("prompts", "conscience", "analyzer", "prompt.md");
    public static readonly string ChatAgentSystemPromptPath = Path.Combine("prompts", "chat", "agent-system", "prompt.md");
}
