using CognitiveMemory.Application.AI.Plugins;
using CognitiveMemory.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CognitiveMemory.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IMemoryService, MemoryService>();
        services.AddScoped<IDocumentIngestionPipeline, DocumentIngestionPipeline>();
        services.AddScoped<AgentToolingGuard>();
        services.AddSingleton<ClaimExtractionPlugin>();
        services.AddSingleton<DebateRolePlugin>();
        services.AddScoped<MemoryRecallPlugin>();
        services.AddScoped<MemoryWritePlugin>();
        services.AddScoped<MemoryGovernancePlugin>();
        services.AddScoped<GroundingPlugin>();
        services.AddScoped<IDebateOrchestrator, SemanticKernelDebateOrchestrator>();

        return services;
    }
}
