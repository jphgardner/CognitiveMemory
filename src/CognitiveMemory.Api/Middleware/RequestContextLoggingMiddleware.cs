using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Api.Middleware;

public sealed class RequestContextLoggingMiddleware(RequestDelegate next, ILogger<RequestContextLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = context.TraceIdentifier,
            ["TraceId"] = traceId
        }))
        {
            logger.LogInformation("Handling HTTP {Method} {Path}", context.Request.Method, context.Request.Path);
            await next(context);
            logger.LogInformation("Completed HTTP {Method} {Path} with status {StatusCode}", context.Request.Method, context.Request.Path, context.Response.StatusCode);
        }
    }
}
