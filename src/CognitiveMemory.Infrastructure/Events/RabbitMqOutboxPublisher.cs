using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace CognitiveMemory.Infrastructure.Events;

public sealed class RabbitMqOutboxPublisher(
    EventDrivenOptions options,
    ILogger<RabbitMqOutboxPublisher> logger) : IOutboxPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task PublishAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        var rabbit = options.RabbitMq;
        var factory = new ConnectionFactory
        {
            HostName = rabbit.HostName,
            Port = rabbit.Port,
            UserName = rabbit.UserName,
            Password = rabbit.Password,
            VirtualHost = rabbit.VirtualHost,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true
        };

        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        if (rabbit.AutoProvisionTopology)
        {
            channel.ExchangeDeclare(rabbit.Exchange, ExchangeType.Topic, durable: rabbit.Durable, autoDelete: false);
            channel.QueueDeclare(rabbit.Queue, durable: rabbit.Durable, exclusive: false, autoDelete: false);
            channel.QueueBind(rabbit.Queue, rabbit.Exchange, $"{rabbit.RoutingKeyPrefix}.#");
        }

        var routingKey = $"{rabbit.RoutingKeyPrefix}.{NormalizeRoutingSegment(@event.EventType)}";
        var payload = JsonSerializer.SerializeToUtf8Bytes(@event, JsonOptions);
        var properties = channel.CreateBasicProperties();
        properties.Persistent = rabbit.Durable;
        properties.ContentType = "application/json";
        properties.Type = @event.EventType;
        properties.MessageId = @event.EventId.ToString("N");
        properties.Timestamp = new AmqpTimestamp(@event.OccurredAtUtc.ToUnixTimeSeconds());

        channel.BasicPublish(
            exchange: rabbit.Exchange,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: payload);

        logger.LogDebug(
            "Published RabbitMQ event. EventId={EventId} Type={Type} RoutingKey={RoutingKey}",
            @event.EventId,
            @event.EventType,
            routingKey);

        return Task.CompletedTask;
    }

    private static string NormalizeRoutingSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch) && i > 0)
            {
                sb.Append('.');
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is '_' or '-' or '.')
            {
                sb.Append('.');
            }
        }

        return sb.ToString().Trim('.');
    }
}
