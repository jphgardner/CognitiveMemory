using Microsoft.Extensions.Options;

namespace CognitiveMemory.Application.AI.Plugins;

public sealed class AgentToolingGuard(IOptions<AgentToolingOptions> options)
{
    private int _toolCallCount;
    private readonly SemaphoreSlim _executionLock = new(1, 1);

    public void EnsureWriteEnabled()
    {
        EnsurePermissionAtLeast(AgentPermissionModes.ReadWriteSafe, "Write tools are disabled by configuration.");
    }

    public void EnsurePrivilegedWriteEnabled()
    {
        EnsurePermissionAtLeast(AgentPermissionModes.Privileged, "Privileged write tools are disabled by configuration.");
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        var current = Interlocked.Increment(ref _toolCallCount);
        if (current > Math.Max(1, options.Value.MaxToolCallsPerRequest))
        {
            throw new InvalidOperationException("Tool invocation limit exceeded for this request.");
        }

        await _executionLock.WaitAsync(cancellationToken);
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, options.Value.ToolTimeoutSeconds)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            return await action(linked.Token);
        }
        finally
        {
            _executionLock.Release();
        }
    }

    private void EnsurePermissionAtLeast(string requiredMode, string errorMessage)
    {
        var currentMode = NormalizeMode(options.Value);
        if (Rank(currentMode) < Rank(requiredMode))
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    private static string NormalizeMode(AgentToolingOptions value)
    {
        var configured = value.Mode?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var match = AgentPermissionModes.All.FirstOrDefault(m => string.Equals(m, configured, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        if (value.EnableWrites == true)
        {
            return AgentPermissionModes.ReadWriteSafe;
        }

        return AgentPermissionModes.ReadOnly;
    }

    private static int Rank(string mode)
    {
        if (string.Equals(mode, AgentPermissionModes.Privileged, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(mode, AgentPermissionModes.ReadWriteSafe, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }
}
