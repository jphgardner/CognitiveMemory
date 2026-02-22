using System.Security.Claims;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Api.Security;

public sealed class CompanionOwnershipService
{
    public async Task<CompanionEntity?> ResolveOwnedCompanionAsync(
        ClaimsPrincipal principal,
        Guid companionId,
        MemoryDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var userId = ResolveUserId(principal);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return await dbContext.Companions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.CompanionId == companionId
                     && !x.IsArchived
                     && x.UserId == userId,
                cancellationToken);
    }

    public async Task<CompanionEntity?> ResolveOwnedCompanionBySessionAsync(
        ClaimsPrincipal principal,
        string sessionId,
        MemoryDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var userId = ResolveUserId(principal);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var normalizedSessionId = sessionId.Trim();
        return await dbContext.Companions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.SessionId == normalizedSessionId
                     && !x.IsArchived
                     && x.UserId == userId,
                cancellationToken);
    }

    private static string? ResolveUserId(ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? principal.FindFirstValue(ClaimTypes.Name);
}
