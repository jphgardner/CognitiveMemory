using CognitiveMemory.Application.AI;
using CognitiveMemory.Domain.Entities;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class DocumentRepository(MemoryDbContext dbContext) : IDocumentRepository
{
    public Task<SourceDocument?> GetByIdAsync(Guid documentId, CancellationToken cancellationToken)
    {
        return dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);
    }

    public Task<SourceDocument?> GetBySourceRefAsync(string sourceType, string sourceRef, CancellationToken cancellationToken)
    {
        return dbContext.Documents.FirstOrDefaultAsync(
            d => d.SourceType == sourceType && d.SourceRef == sourceRef,
            cancellationToken);
    }

    public Task<SourceDocument?> GetBySourceHashAsync(string sourceType, string sourceRef, string contentHash, CancellationToken cancellationToken)
    {
        return dbContext.Documents.FirstOrDefaultAsync(
            d => d.SourceType == sourceType && d.SourceRef == sourceRef && d.ContentHash == contentHash,
            cancellationToken);
    }

    public async Task<SourceDocument> CreateAsync(string sourceType, string sourceRef, string content, string metadata, string contentHash, CancellationToken cancellationToken)
    {
        var document = new SourceDocument
        {
            DocumentId = Guid.NewGuid(),
            SourceType = sourceType,
            SourceRef = sourceRef,
            Content = content,
            Metadata = metadata,
            ContentHash = contentHash,
            CapturedAt = DateTimeOffset.UtcNow
        };

        dbContext.Documents.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        return document;
    }
}
