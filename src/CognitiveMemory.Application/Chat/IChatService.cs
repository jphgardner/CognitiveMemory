namespace CognitiveMemory.Application.Chat;

public interface IChatService
{
    Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ChatStreamChunk> AskStreamAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
