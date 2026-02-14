using CognitiveMemory.Application;
using CognitiveMemory.Api.Endpoints;
using CognitiveMemory.Api.Middleware;
using CognitiveMemory.Infrastructure;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Api.Configuration;
using CognitiveMemory.Application.AI.Plugins;
using CognitiveMemory.Api.Background;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructure();
builder.Services.AddApplication();
builder.Services.Configure<OpenAiCompatOptions>(builder.Configuration.GetSection(OpenAiCompatOptions.SectionName));
builder.Services.Configure<AgentToolingOptions>(builder.Configuration.GetSection(AgentToolingOptions.SectionName));
builder.Services.Configure<ChatPersistenceOptions>(builder.Configuration.GetSection(ChatPersistenceOptions.SectionName));
builder.Services.Configure<ConscienceOutboxWorkerOptions>(builder.Configuration.GetSection(ConscienceOutboxWorkerOptions.SectionName));
builder.Services.Configure<ConscienceCalibrationOptions>(builder.Configuration.GetSection(ConscienceCalibrationOptions.SectionName));
builder.Services.AddHostedService<ConscienceOutboxWorker>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseRequestContextLogging();
await app.Services.ApplyDatabaseMigrationsAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();
app.MapMemoryEndpoints();
app.MapContractEndpoints();
app.MapOpenAiCompatEndpoints();
app.MapConscienceEndpoints();
app.MapOperationsEndpoints();

app.Run();
