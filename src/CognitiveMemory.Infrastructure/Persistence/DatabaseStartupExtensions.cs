using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Persistence;

public static class DatabaseStartupExtensions
{
    public static async Task ApplyDatabaseMigrationsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("CognitiveMemory.Infrastructure.Persistence.DatabaseStartup");

        var dbContext = scope.ServiceProvider.GetRequiredService<MemoryDbContext>();

        try
        {
            logger.LogInformation("Applying pending database migrations (if any).");
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migration step completed.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No migrations were found", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("No EF Core migrations were found. Falling back to EnsureCreated().");
            await dbContext.Database.EnsureCreatedAsync();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("PendingModelChangesWarning", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                ex,
                "Pending EF model changes detected. Skipping automatic migration at startup. Generate and apply a migration to bring the model snapshot in sync.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration startup step failed.");
            throw;
        }
    }
}
