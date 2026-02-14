using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Application.Contracts;

public sealed class AnswerQuestionRequest
{
    [Required]
    public string Question { get; set; } = string.Empty;

    public Dictionary<string, string> Context { get; set; } = [];
}
