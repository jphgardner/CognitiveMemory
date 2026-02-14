using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class InMemoryChatCompletionService : IChatCompletionService
{
    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChatMessageContent> result =
        [
            new(AuthorRole.Assistant, "InMemory semantic-kernel stub response")
        ];

        return Task.FromResult(result);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, "InMemory semantic-kernel stub response");
        await Task.CompletedTask;
    }
}
