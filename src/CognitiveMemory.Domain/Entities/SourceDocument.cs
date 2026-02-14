using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Domain.Entities;

public class SourceDocument
{
    [Key]
    public Guid DocumentId { get; set; }

    [MaxLength(32)]
    public string SourceType { get; set; } = "Other";

    [MaxLength(256)]
    public string SourceRef { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    [MaxLength(128)]
    public string ContentHash { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; }

    public string Metadata { get; set; } = "{}";
}
