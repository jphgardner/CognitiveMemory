namespace CognitiveMemory.Infrastructure.Events;

public sealed class EventDrivenOptions
{
    public bool Enabled { get; set; } = true;
    public string Transport { get; set; } = "InProcess";
    public int PollIntervalSeconds { get; set; } = 2;
    public int BatchSize { get; set; } = 50;
    public int MaxRetries { get; set; } = 8;
    public int SlaWarningLagSeconds { get; set; } = 15;
    public int SlaErrorLagSeconds { get; set; } = 45;
    public Dictionary<string, EventSlaOverrideOptions> SlaByEventType { get; set; } = new();
    public DeadLetterRecoveryOptions DeadLetterRecovery { get; set; } = new();
    public RabbitMqEventingOptions RabbitMq { get; set; } = new();
}

public sealed class EventSlaOverrideOptions
{
    public int? WarningLagSeconds { get; set; }
    public int? ErrorLagSeconds { get; set; }
}

public sealed class DeadLetterRecoveryOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 10;
    public int ReplayBatchSize { get; set; } = 50;
}

public sealed class RabbitMqEventingOptions
{
    public bool Enabled { get; set; } = false;
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string Exchange { get; set; } = "cognitivememory.events";
    public string Queue { get; set; } = "cognitivememory.events.q";
    public string RoutingKeyPrefix { get; set; } = "memory";
    public ushort PrefetchCount { get; set; } = 20;
    public bool Durable { get; set; } = true;
    public bool AutoProvisionTopology { get; set; } = true;
}
