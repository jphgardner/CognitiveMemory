using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Domain.Entities;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class ToolExecutionRepository(MemoryDbContext dbContext) : IToolExecutionRepository
{
    public async Task<ToolExecutionRecord?> GetAsync(string toolName, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var row = await dbContext.ToolExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ToolName == toolName && x.IdempotencyKey == idempotencyKey, cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new ToolExecutionRecord
        {
            ExecutionId = row.ExecutionId,
            ToolName = row.ToolName,
            IdempotencyKey = row.IdempotencyKey,
            ResponseJson = row.ResponseJson,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    public async Task SaveAsync(string toolName, string idempotencyKey, string responseJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return;
        }

        var existing = await dbContext.ToolExecutions
            .FirstOrDefaultAsync(x => x.ToolName == toolName && x.IdempotencyKey == idempotencyKey, cancellationToken);

        if (existing is null)
        {
            dbContext.ToolExecutions.Add(new ToolExecution
            {
                ExecutionId = Guid.NewGuid(),
                ToolName = toolName,
                IdempotencyKey = idempotencyKey,
                ResponseJson = responseJson,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.ResponseJson = responseJson;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent idempotency writes are acceptable; the first persisted response wins.
        }
    }
}
