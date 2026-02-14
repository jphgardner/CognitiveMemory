using CognitiveMemory.Domain.Entities;

namespace CognitiveMemory.Application.AI;

public interface IDocumentRepository
{
    Task<SourceDocument?> GetByIdAsync(Guid documentId, CancellationToken cancellationToken);

    Task<SourceDocument?> GetBySourceRefAsync(string sourceType, string sourceRef, CancellationToken cancellationToken);

    Task<SourceDocument?> GetBySourceHashAsync(string sourceType, string sourceRef, string contentHash, CancellationToken cancellationToken);

    Task<SourceDocument> CreateAsync(string sourceType, string sourceRef, string content, string metadata, string contentHash, CancellationToken cancellationToken);
}
