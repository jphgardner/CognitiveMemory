using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Application.Interfaces;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CognitiveMemory.Infrastructure.Services;

public class SystemHealthProbe(
    MemoryDbContext dbContext,
    IConnectionMultiplexer redis,
    ISemanticKernelHealthProbe semanticKernelHealthProbe,
    ILogger<SystemHealthProbe> logger) : ISystemHealthProbe
{
    public async Task<MemoryHealthResponse> CheckAsync(CancellationToken cancellationToken)
    {
        var dbHealthy = await dbContext.Database.CanConnectAsync(cancellationToken);
        var cacheStatus = "ok";
        double cacheLatencyMs = 0;

        try
        {
            var latency = await redis.GetDatabase().PingAsync();
            cacheLatencyMs = latency.TotalMilliseconds;
        }
        catch (RedisException ex)
        {
            cacheStatus = "unavailable";
            logger.LogWarning(ex, "Redis health probe failed.");
        }

        var modelStatus = semanticKernelHealthProbe.GetStatus();
        logger.LogInformation(
            "Memory health check completed. Database={DatabaseHealth}, Cache={CacheHealth}, Model={ModelHealth}, Provider={Provider}",
            dbHealthy ? "ok" : "unavailable",
            cacheStatus,
            modelStatus.ModelStatus,
            modelStatus.Provider);

        return new MemoryHealthResponse
        {
            Database = dbHealthy ? "ok" : "unavailable",
            Cache = cacheStatus,
            CacheLatencyMs = cacheLatencyMs,
            Model = modelStatus.ModelStatus,
            ModelProvider = modelStatus.Provider
        };
    }
}
