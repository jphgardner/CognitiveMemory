using CognitiveMemory.Application.Contracts;

namespace CognitiveMemory.Application.Interfaces;

public interface IQueryCache
{
    Task<QueryClaimsResponse?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, QueryClaimsResponse value, TimeSpan ttl, CancellationToken cancellationToken);
}
