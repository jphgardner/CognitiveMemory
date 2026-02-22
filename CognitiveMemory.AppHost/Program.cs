using Aspire.Hosting;
using Aspire.Hosting.Yarp;
using Yarp.ReverseProxy.Forwarder;

var builder = DistributedApplication.CreateBuilder(args);

var postgresDataPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "data", "db"));
Directory.CreateDirectory(postgresDataPath);
var rabbitMqDataPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "data", "rabbitmq"));
Directory.CreateDirectory(rabbitMqDataPath);

var postgres = builder
    .AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithHostPort(5432)
    .WithDataBindMount(postgresDataPath)
    .WithPgAdmin(pgAdmin => pgAdmin.WithHostPort(5050));

var memoryDb = postgres.AddDatabase("memorydb");
var cache = builder.AddRedis("cache");
var rabbitMq = builder.AddContainer("rabbitmq", "rabbitmq", "3.13-management")
    .WithEnvironment("RABBITMQ_DEFAULT_USER", "guest")
    .WithEnvironment("RABBITMQ_DEFAULT_PASS", "guest")
    .WithBindMount(rabbitMqDataPath, "/var/lib/rabbitmq")
    .WithEndpoint(name: "amqp", port: 5672, targetPort: 5672)
    .WithEndpoint(name: "management", port: 15672, targetPort: 15672);

var api = builder.AddProject<Projects.CognitiveMemory_Api>("api")
    .WithReference(memoryDb)
    .WithReference(cache)
    .WithEnvironment("EventDriven__Enabled", "true")
    .WithEnvironment("EventDriven__Transport", "RabbitMq")
    .WithEnvironment("EventDriven__RabbitMq__Enabled", "true")
    .WithEnvironment("EventDriven__RabbitMq__HostName", "localhost")
    .WithEnvironment("EventDriven__RabbitMq__Port", "5672")
    .WithEnvironment("EventDriven__RabbitMq__UserName", "guest")
    .WithEnvironment("EventDriven__RabbitMq__Password", "guest")
    .WithEnvironment("EventDriven__RabbitMq__VirtualHost", "/")
    .WaitFor(memoryDb)
    .WaitFor(cache)
    .WaitFor(rabbitMq);

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
