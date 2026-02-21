using CognitiveMemory.Application;
using CognitiveMemory.Application.Consolidation;
using CognitiveMemory.Application.Identity;
using CognitiveMemory.Application.Planning;
using CognitiveMemory.Application.Reasoning;
using CognitiveMemory.Application.Truth;
using CognitiveMemory.Api.Endpoints;
using CognitiveMemory.Api.Background;
using CognitiveMemory.Api.Middleware;
using CognitiveMemory.Infrastructure;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructure();
builder.Services.AddApplication();
builder.Services.AddSingleton(builder.Configuration.GetSection("Consolidation").Get<ConsolidationOptions>() ?? new ConsolidationOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("ConsolidationWorker").Get<ConsolidationWorkerOptions>() ?? new ConsolidationWorkerOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("DecayWorker").Get<DecayWorkerOptions>() ?? new DecayWorkerOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Reasoning").Get<CognitiveReasoningOptions>() ?? new CognitiveReasoningOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("ReasoningWorker").Get<ReasoningWorkerOptions>() ?? new ReasoningWorkerOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Planning").Get<GoalPlanningOptions>() ?? new GoalPlanningOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("IdentityEvolution").Get<IdentityEvolutionOptions>() ?? new IdentityEvolutionOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("IdentityEvolutionWorker").Get<IdentityEvolutionWorkerOptions>() ?? new IdentityEvolutionWorkerOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("TruthMaintenance").Get<TruthMaintenanceOptions>() ?? new TruthMaintenanceOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("TruthMaintenanceWorker").Get<TruthMaintenanceWorkerOptions>() ?? new TruthMaintenanceWorkerOptions());
builder.Services.AddHostedService<ConsolidationWorker>();
builder.Services.AddHostedService<DecayWorker>();
builder.Services.AddHostedService<ReasoningWorker>();
builder.Services.AddHostedService<IdentityEvolutionWorker>();
builder.Services.AddHostedService<TruthMaintenanceWorker>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        _ => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: "global",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseMiddleware<RequestMetricsMiddleware>();
app.UseRequestContextLogging();
if (!app.Environment.IsEnvironment("Test"))
{
    await app.Services.ApplyDatabaseMigrationsAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();
app.MapChatEndpoints();
app.MapEpisodicMemoryEndpoints();
app.MapProceduralMemoryEndpoints();
app.MapSelfModelEndpoints();
app.MapSemanticMemoryEndpoints();
app.MapConsolidationEndpoints();
app.MapToolInvocationAuditEndpoints();
app.MapCognitiveReasoningEndpoints();
app.MapPlanningEndpoints();
app.MapIdentityEvolutionEndpoints();
app.MapTruthMaintenanceEndpoints();

app.Run();

public partial class Program;
