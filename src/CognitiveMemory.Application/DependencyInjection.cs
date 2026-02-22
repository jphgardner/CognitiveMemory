
using CognitiveMemory.Application.Chat;
using CognitiveMemory.Application.Consolidation;
using CognitiveMemory.Application.Episodic;
using CognitiveMemory.Application.Identity;
using CognitiveMemory.Application.Planning;
using CognitiveMemory.Application.Procedural;
using CognitiveMemory.Application.Reasoning;
using CognitiveMemory.Application.Semantic;
using CognitiveMemory.Application.SelfModel;
using CognitiveMemory.Application.Relationships;
using CognitiveMemory.Application.Truth;
using Microsoft.Extensions.DependencyInjection;

namespace CognitiveMemory.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IConsolidationService, ConsolidationService>();
        services.AddScoped<ICognitiveReasoningService, CognitiveReasoningService>();
        services.AddScoped<IEpisodicMemoryService, EpisodicMemoryService>();
        services.AddScoped<IGoalPlanningService, GoalPlanningService>();
        services.AddScoped<IIdentityEvolutionService, IdentityEvolutionService>();
        services.AddScoped<IProceduralMemoryService, ProceduralMemoryService>();
        services.AddScoped<ISemanticMemoryService, SemanticMemoryService>();
        services.AddScoped<IMemoryRelationshipService, MemoryRelationshipService>();
        services.AddScoped<ISelfModelService, SelfModelService>();
        services.AddScoped<ITruthMaintenanceService, TruthMaintenanceService>();

        return services;
    }
}
