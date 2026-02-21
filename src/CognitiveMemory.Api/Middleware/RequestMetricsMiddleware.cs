using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CognitiveMemory.Api.Middleware;

public sealed class RequestMetricsMiddleware(RequestDelegate next)
{
    private static readonly Meter Meter = new("CognitiveMemory.Api");
    private static readonly Histogram<double> RequestDurationMs = Meter.CreateHistogram<double>("http.server.request.duration.ms");
    private static readonly Counter<long> RequestErrors = Meter.CreateCounter<long>("http.server.request.errors");

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await next(context);
            if (context.Response.StatusCode >= 500)
            {
                RequestErrors.Add(1);
            }
        }
        catch
        {
            RequestErrors.Add(1);
            throw;
        }
        finally
        {
            sw.Stop();
            RequestDurationMs.Record(sw.Elapsed.TotalMilliseconds);
        }
    }
}
