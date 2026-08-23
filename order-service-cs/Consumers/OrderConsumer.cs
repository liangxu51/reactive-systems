using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderService.Api.Domain;
using OrderService.Api.Observability;
using OrderService.Api.Producers;
using OrderService.Api.Repositories;
using OrderService.Api.Serialization;

namespace OrderService.Api.Consumers;

/// <summary>
/// Consumes the shared "orders" Kafka topic and drives the order saga.
/// Mirrors order-service (Java)'s @KafkaListener-based OrderConsumer plus its
/// KafkaConsumerConfig (concurrency=6, FixedBackOff(1000, 3) +
/// DeadLetterPublishingRecoverer): six concurrent poll loops in the "orders"
/// consumer group, each retrying a failing message up to 3 times with a 1s
/// fixed backoff before forwarding it unmodified to "orders.DLT" and moving
/// on (committing the offset either way, so a poison-pill message never
/// wedges the consumer).
///
/// The actual per-message saga logic lives in <see cref="OrderMessageHandler"/>
/// so it can be unit tested without a real Kafka broker or IConsumer - this
/// class owns only the Kafka wiring (subscribe/consume/commit) and the
/// retry/DLT envelope around it.
/// </summary>
public sealed class OrderConsumer : BackgroundService
{
    private const string Topic = "orders";
    private const string ConsumerGroup = "orders";
    private const string DeadLetterTopic = "orders.DLT";
    private const int ConcurrentConsumers = 6; // matches the "orders" topic's 6 partitions
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(1);

    private readonly OrderMessageHandler _handler;
    private readonly IOrderProducer _orderProducer;
    private readonly ILogger<OrderConsumer> _logger;
    private readonly string _bootstrapServers;

    public OrderConsumer(
        IOrderRepository orderRepository,
        IProcessedEventRepository processedEventRepository,
        IOrderProducer orderProducer,
        ILoggerFactory loggerFactory,
        string bootstrapServers = "localhost:9092")
    {
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(processedEventRepository);
        ArgumentNullException.ThrowIfNull(orderProducer);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _orderProducer = orderProducer;
        _handler = new OrderMessageHandler(
            orderRepository,
            processedEventRepository,
            orderProducer,
            loggerFactory.CreateLogger<OrderMessageHandler>());
        _logger = loggerFactory.CreateLogger<OrderConsumer>();
        _bootstrapServers = bootstrapServers;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One dedicated thread per worker: Confluent.Kafka's IConsumer.Consume
        // is a blocking call with no async overload, so each loop runs on its
        // own long-lived background thread rather than the thread pool.
        var workers = Enumerable.Range(0, ConcurrentConsumers)
            .Select(_ => Task.Factory.StartNew(
                () => RunConsumeLoop(stoppingToken),
                stoppingToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private void RunConsumeLoop(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = ConsumerGroup,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Latest,
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(Topic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);
                    if (result?.Message is null)
                    {
                        continue;
                    }

                    ProcessWithRetry(result, stoppingToken);
                    consumer.Commit(result);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is the only reason this loop should end -
                    // stoppingToken.IsCancellationRequested is now true, so
                    // the while condition above ends the loop.
                }
                catch (Exception ex)
                {
                    // A transient broker/coordinator hiccup - from Consume,
                    // from Commit, or from an exception escaping
                    // ProcessWithRetry's own DLT-publish catch block - must
                    // not permanently kill this worker. Task.WhenAll in
                    // ExecuteAsync only completes once every worker's Task
                    // completes, so an unhandled fault here would otherwise
                    // silently degrade the pipeline from 6 workers to fewer,
                    // invisible until the whole BackgroundService shuts
                    // down. Log and keep polling; only cancellation ends the
                    // loop.
                    _logger.LogError(ex, "Unhandled error in consume loop for topic {Topic} - continuing", Topic);
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    /// <summary>
    /// Runs the message through <see cref="OrderMessageHandler.HandleAsync"/>,
    /// retrying up to <see cref="MaxAttempts"/> times with a fixed
    /// <see cref="RetryBackoff"/> between attempts. On exhausting retries,
    /// forwards the raw failing message - unmodified - to
    /// <see cref="DeadLetterTopic"/> so the caller can still commit the
    /// offset and move on instead of retrying forever.
    /// </summary>
    private void ProcessWithRetry(ConsumeResult<string, string> result, CancellationToken stoppingToken)
    {
        // One span per consumed message (covering every retry attempt and,
        // on exhaustion, the DLT forward) - matches order-service (Java)'s
        // spring.kafka.listener.observation-enabled=true giving every
        // @KafkaListener invocation its own span.
        using var activity = Telemetry.ActivitySource.StartActivity("kafka.consume", ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", Topic);
        activity?.SetTag("messaging.kafka.message.key", result.Message.Key);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var order = JsonSerializer.Deserialize<Order>(result.Message.Value, OrderJsonOptions.Default)
                    ?? throw new InvalidOperationException("Deserialized order message was null.");
                _handler.HandleAsync(order, stoppingToken).GetAwaiter().GetResult();
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Attempt {Attempt}/{MaxAttempts} failed for message with key {Key} - retrying in {Backoff}",
                    attempt,
                    MaxAttempts,
                    result.Message.Key,
                    RetryBackoff);
                Thread.Sleep(RetryBackoff);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Exhausted {MaxAttempts} attempts for message with key {Key} - publishing to dead-letter topic {DeadLetterTopic}",
                    MaxAttempts,
                    result.Message.Key,
                    DeadLetterTopic);
                _orderProducer.PublishRaw(DeadLetterTopic, result.Message.Key, result.Message.Value);
            }
        }
    }
}
