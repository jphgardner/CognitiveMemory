using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CognitiveMemory.Api.Configuration;
using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.AI.Plugins;
using Microsoft.Extensions.Options;

namespace CognitiveMemory.Api.Endpoints;

public static partial class OpenAiCompatEndpoints
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex NameFactPattern = new(
        @"\b(?:my name is|i am|i'm)\s+([a-z][a-z\s'\-]{0,48})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const int PrefetchPrimaryTimeoutMs = 850;
    private const int PrefetchFallbackTimeoutMs = 950;
    private const int PrefetchDetailTimeoutMs = 500;

    public static IEndpointRouteBuilder MapOpenAiCompatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1");

        group.MapGet("/models", (HttpContext context, IOptions<OpenAiCompatOptions> options) =>
        {
            if (!options.Value.Enabled)
            {
                return Results.NotFound();
            }

            if (!TryAuthorize(context, options.Value, out var unauthorized))
            {
                return unauthorized;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var data = options.Value.Models
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(model => new OpenAiModelItem
                {
                    Id = model,
                    Object = "model",
                    Created = now,
                    OwnedBy = "cognitivememory"
                })
                .ToList();

            return Results.Ok(new OpenAiModelListResponse
            {
                Object = "list",
                Data = data
            });
        });

        group.MapGet("/models/{id}", (string id, HttpContext context, IOptions<OpenAiCompatOptions> options) =>
        {
            if (!options.Value.Enabled)
            {
                return Results.NotFound();
            }

            if (!TryAuthorize(context, options.Value, out var unauthorized))
            {
                return unauthorized;
            }

            if (!options.Value.Models.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                return Results.NotFound(CreateError("The model does not exist.", "invalid_request_error", "model_not_found"));
            }

            return Results.Ok(new OpenAiModelItem
            {
                Id = id,
                Object = "model",
                Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                OwnedBy = "cognitivememory"
            });
        });

        group.MapPost("/chat/completions", async (
            OpenAiChatCompletionRequest request,
            HttpContext context,
            IMemoryKernelFactory kernelFactory,
            MemoryRecallPlugin memoryRecallPlugin,
            MemoryWritePlugin memoryWritePlugin,
            MemoryGovernancePlugin memoryGovernancePlugin,
            GroundingPlugin groundingPlugin,
            IOptions<ChatPersistenceOptions> chatPersistenceOptions,
            IOptions<OpenAiCompatOptions> options,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (!options.Value.Enabled)
            {
                return Results.NotFound();
            }

            if (!TryAuthorize(context, options.Value, out var unauthorized))
            {
                return unauthorized;
            }

            if (request.Messages.Count == 0)
            {
                return Results.BadRequest(CreateError("messages is required", "invalid_request_error", "validation_error"));
            }

            var latestUserIndex = FindLatestUserMessageIndex(request.Messages);
            if (latestUserIndex < 0)
            {
                return Results.BadRequest(CreateError("a user message is required", "invalid_request_error", "validation_error"));
            }
            var latestUserMessage = request.Messages[latestUserIndex];
            if (string.IsNullOrWhiteSpace(latestUserMessage.Content))
            {
                return Results.BadRequest(CreateError("a user message is required", "invalid_request_error", "validation_error"));
            }

            var model = string.IsNullOrWhiteSpace(request.Model) ? options.Value.DefaultModel : request.Model;
            var requestId = BuildRequestId();
            var sourceRef = BuildSourceRef(requestId, request.Messages.Count);
            var logger = loggerFactory.CreateLogger("OpenAiCompat.Chat");
            var conversationKey = ResolveConversationKey(request.User);
            var userActorKey = $"chat-user:{conversationKey}";
            var assistantActorKey = $"chat-assistant:{model}";
            var toolCollector = new ToolExecutionCollector();
            var trackedMemoryRecallPlugin = new TrackingMemoryRecallPlugin(memoryRecallPlugin, toolCollector);
            var trackedMemoryWritePlugin = new TrackingMemoryWritePlugin(memoryWritePlugin, toolCollector);
            var trackedMemoryGovernancePlugin = new TrackingMemoryGovernancePlugin(memoryGovernancePlugin, toolCollector);
            var trackedGroundingPlugin = new TrackingGroundingPlugin(groundingPlugin, toolCollector);
            var preloadedMemoryContext = ShouldPrefetchMemory(request.Messages, latestUserMessage.Content)
                ? await PrefetchMemoryContextAsync(
                    trackedMemoryRecallPlugin,
                    latestUserMessage.Content,
                    userActorKey,
                    logger,
                    cancellationToken)
                : "Memory prefetch skipped for this turn.";

            if (request.Stream)
            {
                return Results.Stream(async stream =>
                {
                    await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
                    var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var streamedAnswer = new StringBuilder();

                    var startChunk = new OpenAiChatCompletionChunk
                    {
                        Id = requestId,
                        Object = "chat.completion.chunk",
                        Created = created,
                        Model = model,
                        Choices =
                        [
                            new OpenAiChatCompletionChunkChoice
                            {
                                Index = 0,
                                Delta = new OpenAiChatDelta { Role = "assistant" },
                                FinishReason = null
                            }
                        ]
                    };

                    await WriteSseAsync(writer, startChunk, cancellationToken);

                    var assistantReply = await GenerateChatReplyWithToolsAsync(
                        model,
                        request.Messages,
                        kernelFactory,
                        trackedMemoryRecallPlugin,
                        trackedMemoryWritePlugin,
                        trackedMemoryGovernancePlugin,
                        trackedGroundingPlugin,
                        preloadedMemoryContext,
                        userActorKey,
                        logger,
                        async (delta, ct) =>
                        {
                            if (string.IsNullOrEmpty(delta))
                            {
                                return;
                            }

                            streamedAnswer.Append(delta);
                            var contentChunk = new OpenAiChatCompletionChunk
                            {
                                Id = requestId,
                                Object = "chat.completion.chunk",
                                Created = created,
                                Model = model,
                                Choices =
                                [
                                    new OpenAiChatCompletionChunkChoice
                                    {
                                        Index = 0,
                                        Delta = new OpenAiChatDelta { Content = delta },
                                        FinishReason = null
                                    }
                                ]
                            };

                            await WriteSseAsync(writer, contentChunk, ct);
                        },
                        cancellationToken);

                    var safeAnswerText = EnsureAssistantText(assistantReply, requestId, logger);
                    await ApplyConfiguredChatPersistenceAsync(
                        chatPersistenceOptions.Value,
                        toolCollector,
                        trackedMemoryWritePlugin,
                        requestId,
                        sourceRef,
                        model,
                        latestUserMessage.Content,
                        safeAnswerText,
                        conversationKey,
                        userActorKey,
                        assistantActorKey,
                        logger,
                        cancellationToken);

                    var responseMetadata = BuildChatMetadata(toolCollector.Snapshot(), safeAnswerText);
                    logger.LogInformation(
                        "Chat request {RequestId} completed with {ToolExecutionCount} tool execution(s).",
                        requestId,
                        responseMetadata.ToolExecutions.Count);
                    if (streamedAnswer.Length == 0 && !string.IsNullOrWhiteSpace(safeAnswerText))
                    {
                        streamedAnswer.Append(safeAnswerText);
                        var fallbackChunk = new OpenAiChatCompletionChunk
                        {
                            Id = requestId,
                            Object = "chat.completion.chunk",
                            Created = created,
                            Model = model,
                            Choices =
                            [
                                new OpenAiChatCompletionChunkChoice
                                {
                                    Index = 0,
                                    Delta = new OpenAiChatDelta { Content = safeAnswerText },
                                    FinishReason = null
                                }
                            ]
                        };
                        await WriteSseAsync(writer, fallbackChunk, cancellationToken);
                    }

                    var endChunk = new OpenAiChatCompletionChunk
                    {
                        Id = requestId,
                        Object = "chat.completion.chunk",
                        Created = created,
                        Model = model,
                        Choices =
                        [
                            new OpenAiChatCompletionChunkChoice
                            {
                                Index = 0,
                                Delta = new OpenAiChatDelta
                                {
                                    Metadata = responseMetadata
                                },
                                FinishReason = "stop"
                            }
                        ]
                    };

                    await WriteSseAsync(writer, endChunk, cancellationToken);
                    await writer.WriteAsync("data: [DONE]\n\n");
                    await writer.FlushAsync(cancellationToken);
                }, "text/event-stream");
            }

            var nonStreamingReply = await GenerateChatReplyWithToolsAsync(
                model,
                request.Messages,
                kernelFactory,
                trackedMemoryRecallPlugin,
                trackedMemoryWritePlugin,
                trackedMemoryGovernancePlugin,
                trackedGroundingPlugin,
                preloadedMemoryContext,
                userActorKey,
                logger,
                onDelta: null,
                cancellationToken: cancellationToken);

            var safeNonStreamingAnswer = EnsureAssistantText(nonStreamingReply, requestId, logger);
            await ApplyConfiguredChatPersistenceAsync(
                chatPersistenceOptions.Value,
                toolCollector,
                trackedMemoryWritePlugin,
                requestId,
                sourceRef,
                model,
                latestUserMessage.Content,
                safeNonStreamingAnswer,
                conversationKey,
                userActorKey,
                assistantActorKey,
                logger,
                cancellationToken);

            var responseMetadata = BuildChatMetadata(toolCollector.Snapshot(), safeNonStreamingAnswer);
            logger.LogInformation(
                "Chat request {RequestId} completed with {ToolExecutionCount} tool execution(s).",
                requestId,
                responseMetadata.ToolExecutions.Count);

            return Results.Ok(new OpenAiChatCompletionResponse
            {
                Id = requestId,
                Object = "chat.completion",
                Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Model = model,
                Choices =
                [
                    new OpenAiChatCompletionChoice
                    {
                        Index = 0,
                        Message = new OpenAiChatMessage
                        {
                            Role = "assistant",
                            Content = safeNonStreamingAnswer,
                            Metadata = responseMetadata
                        },
                        FinishReason = "stop"
                    }
                ],
                Usage = BuildUsage(request, safeNonStreamingAnswer)
            });
        });

        group.MapPost("/embeddings", async (
            OpenAiEmbeddingsRequest request,
            HttpContext context,
            ITextEmbeddingProvider embeddingProvider,
            IOptions<OpenAiCompatOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (!options.Value.Enabled)
            {
                return Results.NotFound();
            }

            if (!TryAuthorize(context, options.Value, out var unauthorized))
            {
                return unauthorized;
            }

            if (!options.Value.ExposeEmbeddings)
            {
                return Results.BadRequest(CreateError("embeddings endpoint is disabled", "invalid_request_error", "embeddings_disabled"));
            }

            var inputs = request.GetInputs();
            if (inputs.Count == 0)
            {
                return Results.BadRequest(CreateError("input is required", "invalid_request_error", "validation_error"));
            }

            var data = new List<OpenAiEmbeddingData>();
            for (var i = 0; i < inputs.Count; i++)
            {
                var embedding = await embeddingProvider.GenerateEmbeddingAsync(inputs[i], cancellationToken);
                data.Add(new OpenAiEmbeddingData
                {
                    Object = "embedding",
                    Index = i,
                    Embedding = embedding.ToArray()
                });
            }

            return Results.Ok(new OpenAiEmbeddingsResponse
            {
                Object = "list",
                Data = data,
                Model = string.IsNullOrWhiteSpace(request.Model) ? "cognitivememory-embeddings" : request.Model,
                Usage = new OpenAiEmbeddingUsage
                {
                    PromptTokens = inputs.Sum(EstimateTokenCount),
                    TotalTokens = inputs.Sum(EstimateTokenCount)
                }
            });
        });

        return app;
    }

    private static bool TryAuthorize(HttpContext context, OpenAiCompatOptions options, out IResult? unauthorized)
    {
        unauthorized = null;

        if (!options.RequireApiKey)
        {
            return true;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            unauthorized = Results.Unauthorized();
            return false;
        }

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            unauthorized = Results.Unauthorized();
            return false;
        }

        if (options.AllowAnyNonEmptyApiKey)
        {
            return true;
        }

        if (!options.AllowedApiKeys.Contains(token))
        {
            unauthorized = Results.Unauthorized();
            return false;
        }

        return true;
    }

    private static OpenAiErrorResponse CreateError(string message, string type, string? code)
    {
        return new OpenAiErrorResponse
        {
            Error = new OpenAiError
            {
                Message = message,
                Type = type,
                Code = code
            }
        };
    }

    private static OpenAiUsage BuildUsage(OpenAiChatCompletionRequest request, string answer)
    {
        var promptTokens = request.Messages.Sum(m => EstimateTokenCount(m.Content));
        var completionTokens = EstimateTokenCount(answer);
        return new OpenAiUsage
        {
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = promptTokens + completionTokens
        };
    }

    private static int EstimateTokenCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Math.Max(1, text.Length / 4);
    }

    private static string BuildRequestId()
    {
        return $"chatcmpl-{Guid.NewGuid():N}";
    }

    private static string BuildSourceRef(string requestId, int count)
    {
        return $"compat:{requestId}/turn:{count}";
    }

}
