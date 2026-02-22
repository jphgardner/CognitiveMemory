
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Application.Cognitive;
using CognitiveMemory.Infrastructure.Memory;
using CognitiveMemory.Application.Relationships;
using CognitiveMemory.Infrastructure.Cognitive;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Repositories;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Reactive;
using CognitiveMemory.Infrastructure.Background;
using CognitiveMemory.Infrastructure.Companions;
using CognitiveMemory.Infrastructure.Subconscious;
using CognitiveMemory.Infrastructure.Scheduling;
using CognitiveMemory.Infrastructure.Relationships;
using CognitiveMemory.Infrastructure.SemanticKernel;
using CognitiveMemory.Infrastructure.SemanticKernel.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CognitiveMemory.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder, string databaseResourceName = "memorydb", string cacheResourceName = "cache")
    {
        builder.AddNpgsqlDbContext<MemoryDbContext>(databaseResourceName);
        builder.AddRedisClient(
            cacheResourceName,
            configureOptions: options =>
            {
                options.AbortOnConnectFail = false;

                if (builder.Environment.IsDevelopment())
                {
                    // Local Aspire endpoints can present self-signed certs.
                    options.CertificateValidation += static (_, _, _, _) => true;
                }
            });

        builder.Services.AddSingleton(CreateSemanticKernelOptions(builder.Configuration));
        builder.Services.AddSingleton(CreateSemanticKernelToolingOptions(builder.Configuration));
        builder.Services.AddSingleton(CreateWorkingMemoryOptions(builder.Configuration));
        builder.Services.AddSingleton(CreateEventDrivenOptions(builder.Configuration));
        builder.Services.AddSingleton(CreateSubconsciousDebateOptions(builder.Configuration));
        builder.Services.AddSingleton(CreateScheduledActionOptions(builder.Configuration));
        builder.Services.AddSingleton(CreateMemoryRelationshipOptions(builder.Configuration));
        builder.Services.AddSingleton<SemanticKernelFactory>();
        builder.Services.AddHttpClient<ITextEmbeddingGenerator, HttpTextEmbeddingGenerator>(
            (sp, client) =>
            {
                var skOptions = sp.GetRequiredService<SemanticKernelOptions>();
                var provider = FirstNonEmpty(skOptions.EmbeddingProvider, skOptions.Provider);
                if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
                {
                    client.BaseAddress = new Uri("https://api.openai.com");
                }
                else
                {
                    var endpoint = FirstNonEmpty(skOptions.EmbeddingOllamaEndpoint, skOptions.OllamaEndpoint, "http://localhost:11434");
                    client.BaseAddress = new Uri(endpoint);
                }

                client.Timeout = TimeSpan.FromSeconds(30);
            });
        builder.Services.AddScoped(sp => sp.GetRequiredService<SemanticKernelFactory>().CreateChatKernel());
        builder.Services.AddScoped(sp => new ClaimExtractionKernel(sp.GetRequiredService<SemanticKernelFactory>().CreateClaimExtractionKernel()));
        builder.Services.AddScoped<MemoryToolsPlugin>();
        builder.Services.AddScoped<ICompanionScopeResolver, CompanionScopeResolver>();
        builder.Services.AddScoped<ICompanionDirectory, CompanionDirectory>();
        builder.Services.AddScoped<CompanionCognitiveProfileService>();
        builder.Services.AddScoped<ICompanionCognitiveProfileService>(sp => sp.GetRequiredService<CompanionCognitiveProfileService>());
        builder.Services.AddScoped<ICompanionCognitiveProfileResolver>(sp => sp.GetRequiredService<CompanionCognitiveProfileService>());
        builder.Services.AddScoped<ICompanionCognitiveRuntimeTraceService>(sp => sp.GetRequiredService<CompanionCognitiveProfileService>());
        builder.Services.AddScoped<ILLMChatGateway, SemanticKernelChatGateway>();
        builder.Services.AddScoped<IClaimExtractionGateway, SemanticKernelClaimExtractionGateway>();
        builder.Services.AddScoped<IWorkingMemoryStore, RedisWorkingMemoryStore>();
        builder.Services.AddScoped<IConsolidationStateRepository, ConsolidationStateRepository>();
        builder.Services.AddScoped<IEpisodicMemoryRepository, EpisodicMemoryRepository>();
        builder.Services.AddScoped<IProceduralMemoryRepository, ProceduralMemoryRepository>();
        builder.Services.AddScoped<ISelfModelRepository, SelfModelRepository>();
        builder.Services.AddScoped<ISemanticMemoryRepository, SemanticMemoryRepository>();
        builder.Services.AddScoped<IMemoryRelationshipRepository, MemoryRelationshipRepository>();
        builder.Services.AddScoped<IMemoryRelationshipExtractionService, AiMemoryRelationshipExtractionService>();
        builder.Services.AddSingleton<IRelationshipConfidencePolicy, RelationshipConfidencePolicy>();
        builder.Services.AddScoped<IToolInvocationAuditRepository, ToolInvocationAuditRepository>();
        builder.Services.AddScoped<IScheduledActionStore, ScheduledActionStore>();

        builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();
        builder.Services.AddSingleton<SubconsciousGroupChatManager>();
        builder.Services.AddScoped<ISubconsciousOutcomeValidator, SubconsciousOutcomeValidator>();
        builder.Services.AddScoped<ISubconsciousOutcomeApplier, SubconsciousOutcomeApplier>();
        builder.Services.AddScoped<ISubconsciousDebateService, SubconsciousDebateService>();
        builder.Services.AddScoped<OutboxEventConsumerDispatcher>();
        builder.Services.AddScoped<InProcessOutboxPublisher>();
        builder.Services.AddScoped<RabbitMqOutboxPublisher>();
        builder.Services.AddScoped<IOutboxPublisher>(
            sp =>
            {
                var eventingOptions = sp.GetRequiredService<EventDrivenOptions>();
                if (string.Equals(eventingOptions.Transport, "RabbitMq", StringComparison.OrdinalIgnoreCase)
                    && eventingOptions.RabbitMq.Enabled)
                {
                    return sp.GetRequiredService<RabbitMqOutboxPublisher>();
                }

                return sp.GetRequiredService<InProcessOutboxPublisher>();
            });
        builder.Services.AddScoped<IOutboxEventConsumer, ConsolidationOnEpisodicCreatedConsumer>();
        builder.Services.AddScoped<IOutboxEventConsumer, ReasoningReactiveConsumer>();
        builder.Services.AddScoped<IOutboxEventConsumer, IdentityEvolutionReactiveConsumer>();
        builder.Services.AddScoped<IOutboxEventConsumer, TruthMaintenanceReactiveConsumer>();
        builder.Services.AddScoped<IOutboxEventConsumer, EventingAnalyticsConsumer>();
        builder.Services.AddScoped<IOutboxEventConsumer, SemanticConfidenceRecalcConsumer>();
        builder.Services.AddScoped<IOutboxEventConsumer, MemoryConflictEscalationConsumer>();
        builder.Services.AddScoped<IOutboxEventConsumer, UserProfileProjectionConsumer>();
        builder.Services.AddScoped<IOutboxEventConsumer, RoutineEffectivenessConsumer>();
        builder.Services.AddScoped<IOutboxEventConsumer, EventingSlaConsumer>();
        builder.Services.AddScoped<IOutboxEventConsumer, SubconsciousDebateSchedulerConsumer>();
        builder.Services.AddScoped<IOutboxEventConsumer, MemoryRelationshipAutoLinkConsumer>();
        builder.Services.AddHostedService<OutboxDispatcherWorker>();
        builder.Services.AddHostedService<RabbitMqInboundConsumerWorker>();
        builder.Services.AddHostedService<DeadLetterRecoveryWorker>();
        builder.Services.AddHostedService<SubconsciousDebateWorker>();
        builder.Services.AddHostedService<ScheduledActionWorker>();

        return builder;
    }

    private static SemanticKernelOptions CreateSemanticKernelOptions(IConfiguration configuration)
    {
        var options = new SemanticKernelOptions();
        configuration.GetSection("SemanticKernel").Bind(options);
        options.OpenAiApiKey ??= configuration["OPENAI_API_KEY"];
        options.EmbeddingOpenAiApiKey ??= options.OpenAiApiKey;
        return options;
    }

    private static WorkingMemoryOptions CreateWorkingMemoryOptions(IConfiguration configuration)
    {
        var options = new WorkingMemoryOptions();
        configuration.GetSection("WorkingMemory").Bind(options);
        return options;
    }

    private static SemanticKernelToolingOptions CreateSemanticKernelToolingOptions(IConfiguration configuration)
    {
        var options = new SemanticKernelToolingOptions();
        configuration.GetSection("SemanticKernelTooling").Bind(options);
        return options;
    }

    private static EventDrivenOptions CreateEventDrivenOptions(IConfiguration configuration)
    {
        var options = new EventDrivenOptions();
        configuration.GetSection("EventDriven").Bind(options);
        return options;
    }

    private static SubconsciousDebateOptions CreateSubconsciousDebateOptions(IConfiguration configuration)
    {
        var options = new SubconsciousDebateOptions();
        configuration.GetSection("SubconsciousDebate").Bind(options);
        return options;
    }

    private static ScheduledActionOptions CreateScheduledActionOptions(IConfiguration configuration)
    {
        var options = new ScheduledActionOptions();
        configuration.GetSection("ScheduledActions").Bind(options);
        return options;
    }

    private static MemoryRelationshipOptions CreateMemoryRelationshipOptions(IConfiguration configuration)
    {
        var options = new MemoryRelationshipOptions();
        configuration.GetSection("MemoryRelationships").Bind(options);
        return options;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}
