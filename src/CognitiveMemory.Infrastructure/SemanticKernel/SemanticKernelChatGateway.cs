using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Infrastructure.SemanticKernel.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class SemanticKernelChatGateway(
    Kernel kernel,
    SemanticKernelOptions options,
    SemanticKernelToolingOptions toolingOptions,
    MemoryToolsPlugin memoryToolsPlugin,
    ILogger<SemanticKernelChatGateway> logger) : ILLMChatGateway
{
    public async Task<string> GetCompletionAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
                      <system>
                      {systemPrompt}
                      </system>
                      <user>
                      {userPrompt}
                      </user>
                      """;

        RegisterPluginsIfEnabled();

        var settings = GetExecutionSettings();
        return await InvokePromptWithTimeoutAsync(prompt, settings, GetConfiguredTimeout(), cancellationToken);
    }

    public async IAsyncEnumerable<string> GetCompletionStreamAsync(
        string systemPrompt,
        string userPrompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prompt = $"""
                      <system>
                      {systemPrompt}
                      </system>
                      <user>
                      {userPrompt}
                      </user>
                      """;

        RegisterPluginsIfEnabled();

        var settings = GetExecutionSettings();
        var configuredTimeout = GetConfiguredTimeout();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(configuredTimeout);

        var stream = settings is null
            ? kernel.InvokePromptStreamingAsync(prompt, cancellationToken: timeoutCts.Token)
            : kernel.InvokePromptStreamingAsync(prompt, new KernelArguments(settings), cancellationToken: timeoutCts.Token);

        var streamedAny = false;
        var attemptedFallback = false;
        string? fallback = null;
        await using var enumerator = stream.WithCancellation(timeoutCts.Token).GetAsyncEnumerator();
        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await enumerator.MoveNextAsync();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !streamedAny)
            {
                // Streaming can time out before first token on slower models; fall back to a normal completion.
                attemptedFallback = true;
                fallback = await TryFallbackCompletionAsync(prompt, settings, configuredTimeout, cancellationToken);
                break;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && !streamedAny)
            {
                // Some providers fail streaming but still support standard completion.
                logger.LogWarning(ex, "Streaming invocation failed before first token. Falling back to one-shot completion.");
                attemptedFallback = true;
                fallback = await TryFallbackCompletionAsync(prompt, settings, configuredTimeout, cancellationToken);
                break;
            }

            if (!hasNext)
            {
                break;
            }

            var delta = enumerator.Current?.ToString();
            if (string.IsNullOrWhiteSpace(delta))
            {
                continue;
            }

            streamedAny = true;
            yield return delta;
        }

        if (attemptedFallback && string.IsNullOrWhiteSpace(fallback) && !streamedAny)
        {
            throw new TimeoutException("No streaming tokens were produced before timeout, and fallback completion returned no output.");
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            yield return fallback;
        }
    }

    private TimeSpan GetConfiguredTimeout() => TimeSpan.FromSeconds(Math.Max(5, options.ChatResponseTimeoutSeconds));

    private async Task<string> InvokePromptWithTimeoutAsync(
        string prompt,
        PromptExecutionSettings? settings,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new TimeoutException("The request timeout budget was exhausted before prompt execution.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var result = settings is null
            ? await kernel.InvokePromptAsync(prompt, cancellationToken: timeoutCts.Token)
            : await kernel.InvokePromptAsync(prompt, new KernelArguments(settings), cancellationToken: timeoutCts.Token);

        return result.GetValue<string>()?.Trim() ?? string.Empty;
    }

    private async Task<string?> TryFallbackCompletionAsync(
        string prompt,
        PromptExecutionSettings? settings,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout < TimeSpan.FromSeconds(2))
        {
            logger.LogWarning("Skipping fallback completion because timeout budget is exhausted.");
            return null;
        }

        try
        {
            return await InvokePromptWithTimeoutAsync(prompt, settings, timeout, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Fallback completion timed out with no output.");
            return null;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && settings is not null)
        {
            logger.LogWarning(ex, "Fallback completion with execution settings failed. Retrying without tool settings.");
            try
            {
                return await InvokePromptWithTimeoutAsync(prompt, settings: null, timeout, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Fallback completion without tool settings timed out with no output.");
                return null;
            }
            catch (Exception retryEx) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(retryEx, "Fallback completion without tool settings failed.");
                return null;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Fallback completion failed.");
            return null;
        }
    }

    private void RegisterPluginsIfEnabled()
    {
        if (!toolingOptions.EnableMemoryTools)
        {
            return;
        }

        if (kernel.Plugins.Any(p => string.Equals(p.Name, toolingOptions.PluginName, StringComparison.Ordinal)))
        {
            return;
        }

        kernel.Plugins.AddFromObject(memoryToolsPlugin, toolingOptions.PluginName);
    }

    private PromptExecutionSettings? GetExecutionSettings()
    {
        if (!toolingOptions.EnableMemoryTools || !toolingOptions.AutoInvokeTools)
        {
            return null;
        }

        if (string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            };
        }

        if (string.Equals(options.Provider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            return new OllamaPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };
        }

        return null;
    }
}
