namespace CognitiveMemory.Api.Middleware;

public static class RequestContextLoggingExtensions
{
    public static IApplicationBuilder UseRequestContextLogging(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RequestContextLoggingMiddleware>();
    }
}
