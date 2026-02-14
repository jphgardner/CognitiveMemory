using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Domain.Entities;

public class MemoryEntity
{
    [Key]
    public Guid EntityId { get; set; }

    [MaxLength(32)]
    public string Type { get; set; } = "Concept";

    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    public List<string> Aliases { get; set; } = [];

    public string Metadata { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
