namespace CognitiveMemory.Application.Abstractions;

public interface ILLMChatGateway
{
    Task<string> GetCompletionAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> GetCompletionStreamAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
