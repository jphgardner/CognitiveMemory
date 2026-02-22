using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Companions;

public sealed class CompanionScopeResolver(MemoryDbContext dbContext) : ICompanionScopeResolver
{
    public async Task<Guid?> TryResolveCompanionIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var normalized = sessionId.Trim();
        return await dbContext.Companions
            .AsNoTracking()
            .Where(x => !x.IsArchived && x.SessionId == normalized)
            .Select(x => (Guid?)x.CompanionId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid> ResolveCompanionIdOrThrowAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var companionId = await TryResolveCompanionIdAsync(sessionId, cancellationToken);
        if (!companionId.HasValue)
        {
            throw new InvalidOperationException($"No companion is bound to sessionId '{sessionId}'.");
        }

        return companionId.Value;
    }
}
