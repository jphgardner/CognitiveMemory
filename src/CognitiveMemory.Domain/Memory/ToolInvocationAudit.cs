namespace CognitiveMemory.Domain.Memory;

public sealed record ToolInvocationAudit(
    Guid AuditId,
    string ToolName,
    bool IsWrite,
    string ArgumentsJson,
    string ResultJson,
    bool Succeeded,
    string? Error,
    DateTimeOffset ExecutedAtUtc);
