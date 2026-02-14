using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Application.Contracts;

public sealed class QueryClaimsRequest
{
    [Required]
    public string Text { get; set; } = string.Empty;

    public QueryFilters Filters { get; set; } = new();

    [Range(1, 50)]
    public int TopK { get; set; } = 10;

    public bool IncludeEvidence { get; set; } = true;

    public bool IncludeContradictions { get; set; } = true;
}

public sealed class QueryFilters
{
    public string? Subject { get; set; }
}
