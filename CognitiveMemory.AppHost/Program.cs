using Aspire.Hosting;
using Aspire.Hosting.Yarp;

var builder = DistributedApplication.CreateBuilder(args);

var postgresDataPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "data", "db"));
Directory.CreateDirectory(postgresDataPath);

var postgres = builder
    .AddPostgres("postgres")
    .WithHostPort(5432)
    .WithDataBindMount(postgresDataPath)
    .WithPgAdmin(pgAdmin => pgAdmin.WithHostPort(5050));

var memoryDb = postgres.AddDatabase("memorydb");
var cache = builder.AddRedis("cache");

var api = builder.AddProject<Projects.CognitiveMemory_Api>("api")
    .WithReference(memoryDb)
    .WithReference(cache)
    .WaitFor(memoryDb)
    .WaitFor(cache);

var frontend = builder.AddJavaScriptApp(
        "frontend",
        "../frontend/cognitive-memory-chat",
        "start")
    .WithHttpEndpoint(port: 4210, env: "PORT")
    .WithEnvironment("BROWSER", "none")
    .WaitFor(api);

builder.AddYarp("gateway")
    .WithHostPort(8080)
    .WithConfiguration(config =>
    {
        config.AddRoute("/v1/{**catch-all}", api);
        config.AddRoute("/api/{**catch-all}", api);
        config.AddRoute("/{**catch-all}", frontend);
    })
    .WithReference(api)
    .WithReference(frontend)
    .WaitFor(api)
    .WaitFor(frontend);

builder.Build().Run();
