using System.Text.Json;
using CognitiveMemory.Application.Abstractions;
using StackExchange.Redis;

namespace CognitiveMemory.Infrastructure.Memory;

public sealed class RedisWorkingMemoryStore(
    IConnectionMultiplexer connectionMultiplexer,
    WorkingMemoryOptions options) : IWorkingMemoryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<WorkingMemoryContext> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = connectionMultiplexer.GetDatabase();
        var key = GetKey(sessionId);
        var payload = await db.StringGetAsync(key);

        if (!payload.HasValue)
        {
            return new WorkingMemoryContext(sessionId, Array.Empty<WorkingMemoryTurn>());
        }

        return JsonSerializer.Deserialize<WorkingMemoryContext>(payload.ToString(), SerializerOptions)
               ?? new WorkingMemoryContext(sessionId, Array.Empty<WorkingMemoryTurn>());
    }

    public async Task SaveAsync(WorkingMemoryContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = connectionMultiplexer.GetDatabase();
        var key = GetKey(context.SessionId);
        var payload = JsonSerializer.Serialize(context, SerializerOptions);

        await db.StringSetAsync(
            key,
            payload,
            TimeSpan.FromSeconds(Math.Max(1, options.TtlSeconds)));
    }

    private string GetKey(string sessionId) => $"{options.KeyPrefix}{sessionId}";
}
