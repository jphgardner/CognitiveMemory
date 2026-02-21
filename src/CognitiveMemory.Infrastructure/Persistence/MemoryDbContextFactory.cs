using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CognitiveMemory.Infrastructure.Persistence;

public sealed class MemoryDbContextFactory : IDesignTimeDbContextFactory<MemoryDbContext>
{
    public MemoryDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("COGNITIVE_MEMORY_DB")
            ?? "Host=localhost;Port=5432;Database=cognitivememory;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<MemoryDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new MemoryDbContext(optionsBuilder.Options);
    }
}
