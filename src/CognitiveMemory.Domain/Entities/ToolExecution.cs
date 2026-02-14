using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Domain.Entities;

public sealed class ToolExecution
{
    [Key]
    public Guid ExecutionId { get; set; }

    [MaxLength(128)]
    public string ToolName { get; set; } = string.Empty;

    [MaxLength(256)]
    public string IdempotencyKey { get; set; } = string.Empty;

    public string ResponseJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
