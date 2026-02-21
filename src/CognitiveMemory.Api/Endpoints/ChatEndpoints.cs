using CognitiveMemory.Application.Chat;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Api.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/chat",
                async (ChatRequestDto request, IChatService chatService, CancellationToken cancellationToken) =>
                {
                    var response = await chatService.AskAsync(
                        new ChatRequest(request.Message, request.SessionId),
                        cancellationToken);

                    return Results.Ok(
                        new ChatResponseDto(
                            response.SessionId,
                            response.Answer,
                            response.GeneratedAtUtc,
                            response.ContextTurnCount));
                })
            .WithName("ChatAsk")
            .WithTags("Chat");

        endpoints.MapPost(
                "/api/chat/stream",
                async (HttpContext httpContext, ChatRequestDto request, IChatService chatService, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
                {
                    var logger = loggerFactory.CreateLogger("CognitiveMemory.Api.Endpoints.ChatStream");
                    httpContext.Response.StatusCode = StatusCodes.Status200OK;
                    httpContext.Response.ContentType = "text/event-stream";
                    httpContext.Response.Headers.CacheControl = "no-cache";
                    httpContext.Response.Headers.Append("X-Accel-Buffering", "no");
                    await httpContext.Response.StartAsync(cancellationToken);
                    await WriteSseCommentAsync(httpContext.Response, "stream-open", cancellationToken);

                    var keepAliveInterval = TimeSpan.FromSeconds(10);
                    var streamException = default(Exception);
                    var channel = Channel.CreateUnbounded<ChatStreamChunk>(
                        new UnboundedChannelOptions
                        {
                            SingleReader = true,
                            SingleWriter = true
                        });

                    var producer = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await foreach (var chunk in chatService.AskStreamAsync(
                                                   new ChatRequest(request.Message, request.SessionId),
                                                   cancellationToken))
                                {
                                    await channel.Writer.WriteAsync(chunk, cancellationToken);
                                }
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                // Client disconnected or request was canceled.
                            }
                            catch (Exception ex)
                            {
                                streamException = ex;
                                logger.LogError(ex, "Chat stream failed before completion. SessionId={SessionId}", request.SessionId ?? string.Empty);
                            }
                            finally
                            {
                                channel.Writer.TryComplete();
                            }
                        });

                    var sentFinal = false;
                    while (true)
                    {
                        while (channel.Reader.TryRead(out var chunk))
                        {
                            await WriteSseEventAsync(
                                httpContext.Response,
                                chunk.IsFinal ? "done" : "delta",
                                chunk,
                                cancellationToken);

                            if (chunk.IsFinal)
                            {
                                sentFinal = true;
                            }
                        }

                        if (channel.Reader.Completion.IsCompleted)
                        {
                            break;
                        }

                        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        waitCts.CancelAfter(keepAliveInterval);
                        try
                        {
                            var canRead = await channel.Reader.WaitToReadAsync(waitCts.Token);
                            if (!canRead)
                            {
                                break;
                            }
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            await WriteSseCommentAsync(httpContext.Response, "keep-alive", cancellationToken);
                        }
                    }

                    await producer;

                    if (streamException is not null && !cancellationToken.IsCancellationRequested)
                    {
                        await WriteSseEventAsync(
                            httpContext.Response,
                            "error",
                            new
                            {
                                message = "Stream generation failed.",
                                detail = streamException.Message
                            },
                            cancellationToken);
                    }

                    if (!sentFinal && !cancellationToken.IsCancellationRequested)
                    {
                        await WriteSseEventAsync(
                            httpContext.Response,
                            "done",
                            new
                            {
                                sessionId = request.SessionId ?? string.Empty,
                                delta = string.Empty,
                                isFinal = true,
                                generatedAtUtc = DateTimeOffset.UtcNow,
                                contextTurnCount = 0
                            },
                            cancellationToken);
                    }
                })
            .WithName("ChatAskStream")
            .WithTags("Chat");

        return endpoints;
    }

    private static async Task WriteSseEventAsync(HttpResponse response, string eventName, object payload, CancellationToken cancellationToken)
    {
        var serialized = JsonSerializer.Serialize(payload);
        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync($"data: {serialized}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteSseCommentAsync(HttpResponse response, string comment, CancellationToken cancellationToken)
    {
        await response.WriteAsync($": {comment}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}

public sealed record ChatRequestDto(string Message, string? SessionId = null);
public sealed record ChatResponseDto(
    string SessionId,
    string Answer,
    DateTimeOffset GeneratedAtUtc,
    int ContextTurnCount);
