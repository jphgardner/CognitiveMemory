using Aspire.Hosting;
using Aspire.Hosting.Yarp;
using Yarp.ReverseProxy.Forwarder;

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
        var apiCluster = config
            .AddCluster(api)
            .WithForwarderRequestConfig(
                new ForwarderRequestConfig
                {
                    // Generative streams can have long first-token latency under local models.
                    ActivityTimeout = TimeSpan.FromHours(1),
                    // Buffered proxy responses can break SSE behavior.
                    AllowResponseBuffering = false
                });

        config.AddRoute("/v1/{**catch-all}", apiCluster);
        config.AddRoute("/api/{**catch-all}", apiCluster);
        config.AddRoute("/{**catch-all}", frontend);
    })
    .WithReference(api)
    .WithReference(frontend)
    .WaitFor(api)
    .WaitFor(frontend);

builder.Build().Run();
