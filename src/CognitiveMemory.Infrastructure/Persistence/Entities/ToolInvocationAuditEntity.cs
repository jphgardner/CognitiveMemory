namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class ToolInvocationAuditEntity
{
    public Guid AuditId { get; set; }
    public Guid CompanionId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public bool IsWrite { get; set; }
    public string ArgumentsJson { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset ExecutedAtUtc { get; set; }
}
