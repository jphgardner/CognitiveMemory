using CognitiveMemory.Application.Interfaces;
using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Repositories;
using CognitiveMemory.Infrastructure.SemanticKernel;
using CognitiveMemory.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

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
        builder.Services.Configure<SemanticKernelOptions>(builder.Configuration.GetSection(SemanticKernelOptions.SectionName));
        builder.Services.Configure<QueryCacheOptions>(builder.Configuration.GetSection(QueryCacheOptions.SectionName));

        builder.Services.AddScoped<IClaimRepository, ClaimRepository>();
        builder.Services.AddScoped<IEntityRepository, EntityRepository>();
        builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
        builder.Services.AddScoped<ISystemHealthProbe, SystemHealthProbe>();
        builder.Services.AddScoped<IToolExecutionRepository, ToolExecutionRepository>();
        builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();
        builder.Services.AddScoped<IPolicyDecisionRepository, PolicyDecisionRepository>();
        builder.Services.AddScoped<IClaimInsightRepository, ClaimInsightRepository>();
        builder.Services.AddScoped<IClaimCalibrationRepository, ClaimCalibrationRepository>();
        builder.Services.AddSingleton<IQueryCache, RedisQueryCache>();
        builder.Services.AddScoped<ITextEmbeddingProvider, SemanticKernelEmbeddingProvider>();
        builder.Services.AddSingleton<IMemoryKernelFactory, MemoryKernelFactory>();
        builder.Services.AddSingleton<ISemanticKernelHealthProbe, SemanticKernelHealthProbe>();
        builder.Services.AddScoped<IClaimExtractionEngine, SemanticKernelClaimExtractionEngine>();
        builder.Services.AddScoped<IConscienceAnalysisEngine, SemanticKernelConscienceAnalysisEngine>();

        return builder;
    }
}
