namespace CognitiveMemory.Application.Contracts;

public sealed class ApiErrorResponse
{
    public ApiError Error { get; init; } = new();

    public string RequestId { get; init; } = string.Empty;
}

public sealed class ApiError
{
    public string Code { get; init; } = "VALIDATION_ERROR";

    public string Message { get; init; } = string.Empty;

    public object Details { get; init; } = new { };
}
