using System.Text.Json;
using CognitiveMemory.Infrastructure.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CognitiveMemory.Infrastructure.Background;

public sealed class RabbitMqInboundConsumerWorker(
    EventDrivenOptions options,
    IServiceScopeFactory scopeFactory,
    ILogger<RabbitMqInboundConsumerWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled
            || !string.Equals(options.Transport, "RabbitMq", StringComparison.OrdinalIgnoreCase)
            || !options.RabbitMq.Enabled)
        {
            logger.LogInformation("RabbitMQ inbound consumer disabled.");
            return;
        }

        var rabbit = options.RabbitMq;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
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

                channel.BasicQos(prefetchSize: 0, prefetchCount: rabbit.PrefetchCount, global: false);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += async (_, ea) =>
                {
                    try
                    {
                        var @event = JsonSerializer.Deserialize<OutboxEvent>(ea.Body.Span, JsonOptions);
                        if (@event is null)
                        {
                            logger.LogWarning("RabbitMQ message deserialized to null. DeliveryTag={DeliveryTag}", ea.DeliveryTag);
                            channel.BasicAck(ea.DeliveryTag, multiple: false);
                            return;
                        }

                        using var scope = scopeFactory.CreateScope();
                        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxEventConsumerDispatcher>();
                        await dispatcher.DispatchAsync(@event, stoppingToken);
                        channel.BasicAck(ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "RabbitMQ inbound event processing failed. DeliveryTag={DeliveryTag}", ea.DeliveryTag);
                        channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
                    }
                };

                channel.BasicConsume(queue: rabbit.Queue, autoAck: false, consumer: consumer);

                logger.LogInformation(
                    "RabbitMQ inbound consumer started. Host={Host} Queue={Queue} Exchange={Exchange}",
                    rabbit.HostName,
                    rabbit.Queue,
                    rabbit.Exchange);

                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "RabbitMQ inbound consumer failed to connect/run. Retrying in {DelaySeconds}s. Host={Host} Port={Port} User={User} VHost={VHost}",
                    ReconnectDelay.TotalSeconds,
                    rabbit.HostName,
                    rabbit.Port,
                    rabbit.UserName,
                    rabbit.VirtualHost);

                try
                {
                    await Task.Delay(ReconnectDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
