using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Companions;

public sealed class CompanionDirectory(MemoryDbContext dbContext) : ICompanionDirectory
{
    public async Task<IReadOnlyList<CompanionScope>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Companions
            .AsNoTracking()
            .Where(x => !x.IsArchived)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new CompanionScope(x.CompanionId, x.SessionId, x.UserId))
            .ToListAsync(cancellationToken);
        return rows;
    }

    public async Task<CompanionScope?> GetByCompanionIdAsync(Guid companionId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Companions
            .AsNoTracking()
            .Where(x => !x.IsArchived && x.CompanionId == companionId)
            .Select(x => new CompanionScope(x.CompanionId, x.SessionId, x.UserId))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
