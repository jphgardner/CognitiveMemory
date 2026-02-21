
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Infrastructure.Memory;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Repositories;
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
        builder.Services.AddSingleton<SemanticKernelFactory>();
        builder.Services.AddScoped(sp => sp.GetRequiredService<SemanticKernelFactory>().CreateChatKernel());
        builder.Services.AddScoped(sp => new ClaimExtractionKernel(sp.GetRequiredService<SemanticKernelFactory>().CreateClaimExtractionKernel()));
        builder.Services.AddScoped<MemoryToolsPlugin>();
        builder.Services.AddScoped<ILLMChatGateway, SemanticKernelChatGateway>();
        builder.Services.AddScoped<IClaimExtractionGateway, SemanticKernelClaimExtractionGateway>();
        builder.Services.AddSingleton<IWorkingMemoryStore, RedisWorkingMemoryStore>();
        builder.Services.AddScoped<IConsolidationStateRepository, ConsolidationStateRepository>();
        builder.Services.AddScoped<IEpisodicMemoryRepository, EpisodicMemoryRepository>();
        builder.Services.AddScoped<IProceduralMemoryRepository, ProceduralMemoryRepository>();
        builder.Services.AddScoped<ISelfModelRepository, SelfModelRepository>();
        builder.Services.AddScoped<ISemanticMemoryRepository, SemanticMemoryRepository>();
        builder.Services.AddScoped<IToolInvocationAuditRepository, ToolInvocationAuditRepository>();

        return builder;
    }

    private static SemanticKernelOptions CreateSemanticKernelOptions(IConfiguration configuration)
    {
        var options = new SemanticKernelOptions();
        configuration.GetSection("SemanticKernel").Bind(options);
        options.OpenAiApiKey ??= configuration["OPENAI_API_KEY"];
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
}
