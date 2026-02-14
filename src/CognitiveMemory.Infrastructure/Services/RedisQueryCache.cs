using System.Text.Json;
using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CognitiveMemory.Infrastructure.Services;

public sealed class RedisQueryCache(
    IConnectionMultiplexer redis,
    IOptions<QueryCacheOptions> options,
    ILogger<RedisQueryCache> logger) : IQueryCache
{
    private readonly IDatabase _database = redis.GetDatabase();
    private readonly TimeSpan _defaultTtl = TimeSpan.FromSeconds(Math.Max(1, options.Value.TtlSeconds));

    public async Task<QueryClaimsResponse?> GetAsync(string key, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        RedisValue value;
        try
        {
            value = await _database.StringGetAsync(key);
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis cache read failed for key {CacheKey}. Continuing without cache.", key);
            return null;
        }

        if (!value.HasValue)
        {
            return null;
        }

        var json = value.ToString();
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<QueryClaimsResponse>(json);
    }

    public async Task SetAsync(string key, QueryClaimsResponse value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var payload = JsonSerializer.Serialize(value);

        try
        {
            await _database.StringSetAsync(key, payload, ttl <= TimeSpan.Zero ? _defaultTtl : ttl);
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis cache write failed for key {CacheKey}. Continuing without cache.", key);
        }
    }
}
