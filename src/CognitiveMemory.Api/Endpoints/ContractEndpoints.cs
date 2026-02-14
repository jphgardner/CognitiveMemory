using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Application.Services;

namespace CognitiveMemory.Api.Endpoints;

public static class ContractEndpoints
{
    public static IEndpointRouteBuilder MapContractEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1");

        group.MapPost("/ingest", async (IngestDocumentRequest request, HttpContext context, IMemoryService memoryService, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return Results.BadRequest(context.CreateValidationError("content is required"));
            }

            var result = await memoryService.IngestDocumentAsync(request, cancellationToken);
            return Results.Accepted($"/api/v1/ingest/{result.DocumentId}", new
            {
                documentId = result.DocumentId,
                status = "Queued"
            });
        });

        group.MapPost("/query", async (QueryClaimsRequest request, HttpContext context, IMemoryService memoryService, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.BadRequest(context.CreateValidationError("text is required"));
            }

            if (request.TopK is < 1 or > 50)
            {
                return Results.BadRequest(context.CreateValidationError("topK must be between 1 and 50", new { topK = request.TopK }));
            }

            var response = await memoryService.QueryClaimsAsync(request, context.TraceIdentifier, cancellationToken);
            return Results.Ok(response);
        });

        group.MapPost("/answer", async (AnswerQuestionRequest request, HttpContext context, IMemoryService memoryService, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.BadRequest(context.CreateValidationError("question is required"));
            }

            var response = await memoryService.AnswerAsync(request, context.TraceIdentifier, cancellationToken);
            return Results.Ok(response);
        });

        return app;
    }
}

internal static class ContractEndpointErrors
{
    public static ApiErrorResponse CreateValidationError(this HttpContext context, string message, object? details = null)
    {
        return new ApiErrorResponse
        {
            Error = new ApiError
            {
                Code = "VALIDATION_ERROR",
                Message = message,
                Details = details ?? new { }
            },
            RequestId = context.TraceIdentifier
        };
    }
}
