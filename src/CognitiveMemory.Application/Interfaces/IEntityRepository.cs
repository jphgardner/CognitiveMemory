using System;
using System.Collections.Generic;
using CognitiveMemory.Domain.Entities;

namespace CognitiveMemory.Application.Interfaces;

public interface IEntityRepository
{
    Task<IReadOnlyList<MemoryEntity>> GetRecentAsync(int take, CancellationToken cancellationToken);

    Task<MemoryEntity?> GetByIdAsync(Guid entityId, CancellationToken cancellationToken);

    Task<MemoryEntity> UpsertAsync(
        Guid entityId,
        string type,
        string name,
        IEnumerable<string>? aliases,
        string metadata,
        CancellationToken cancellationToken);
}
