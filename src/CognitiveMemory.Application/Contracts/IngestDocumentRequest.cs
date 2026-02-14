using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Application.Contracts;

public sealed class IngestDocumentRequest
{
    [Required]
    [MaxLength(32)]
    public string SourceType { get; set; } = "Other";

    [Required]
    [MaxLength(256)]
    public string SourceRef { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    public Dictionary<string, string> Metadata { get; set; } = [];
}
