using CognitiveMemory.Domain.Entities;

namespace CognitiveMemory.Application.Services;

public interface IDocumentIngestionPipeline
{
    Task<int> ProcessDocumentAsync(SourceDocument document, CancellationToken cancellationToken);
}
