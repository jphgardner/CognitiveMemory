using System.Security.Claims;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Api.Middleware;

public sealed class DatabaseRlsContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, MemoryDbContext dbContext)
    {
        if (dbContext.Database.IsRelational())
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? context.User.FindFirstValue(ClaimTypes.Name)
                         ?? string.Empty;

            // Enforce RLS for HTTP requests by default; background workers use role-level bypass setting.
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"select set_config('app.bypass_rls', {"false"}, false);");
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"select set_config('app.current_user_id', {userId}, false);");
        }

        await next(context);
    }
}

public static class DatabaseRlsContextMiddlewareExtensions
{
    public static IApplicationBuilder UseDatabaseRlsContext(this IApplicationBuilder app)
        => app.UseMiddleware<DatabaseRlsContextMiddleware>();
}
