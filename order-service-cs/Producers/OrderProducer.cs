using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OrderService.Api.Domain;
using OrderService.Api.Serialization;

namespace OrderService.Api.Producers;

/// <summary>
/// Seam over <see cref="OrderProducer"/> so OrderConsumer's saga message
/// handling (and the producer's own message-shape tests) can run against a
/// mocked/fake producer instead of a real Kafka broker.
/// </summary>
public interface IOrderProducer
{
    /// <summary>
    /// Serializes <paramref name="order"/> with <see cref="OrderJsonOptions.Default"/>
    /// and publishes it to the shared "orders" topic, keyed by the order's id
    /// (lowercase hex). Fire-and-forget: matches order-service (Java)'s
    /// KafkaTemplate.send(...).whenComplete(...) - delivery failures are
    /// logged, never thrown back to the caller.
    /// </summary>
    void SendMessage(Order order);

    /// <summary>
    /// Publishes an already-serialized value unmodified to an arbitrary
    /// topic/key, bypassing Order serialization entirely. Used by
    /// OrderConsumer to forward a poison-pill message byte-for-byte to
    /// "orders.DLT" after its retries are exhausted, matching order-service
    /// (Java)'s DeadLetterPublishingRecoverer, which republishes the raw
    /// failing record rather than re-encoding it.
    /// </summary>
    void PublishRaw(string topic, string key, string? value);
}

/// <summary>
/// Publishes Order messages to the shared "orders" Kafka topic. Mirrors
/// com.baeldung.async.producer.OrderProducer in order-service (Java): same
/// topic, same key (order id as lowercase hex), same acks=all durability,
/// same fire-and-forget send with an error-only completion callback.
///
/// Wraps a Confluent.Kafka IProducer&lt;string, string&gt; rather than using
/// its built-in JSON (de)serializer support: the wire bytes must match
/// Java's Spring JsonSerializer exactly (see Serialization/OrderJsonOptions),
/// so the Order is serialized to a plain string up front and handed to the
/// producer unchanged, keeping exactly one serialization path for both the
/// Kafka and (future) HTTP layers.
/// </summary>
public sealed class OrderProducer : IOrderProducer, IDisposable
{
    public const string Topic = "orders";

    private readonly IProducer<string, string> _producer;
    private readonly ILogger<OrderProducer> _logger;

    public OrderProducer(IProducer<string, string> producer, ILogger<OrderProducer> logger)
    {
        _producer = producer;
        _logger = logger;
    }

    public void SendMessage(Order order)
    {
        // Order.ToString() is overridden to log only id/status/item-count
        // (see Domain/Order.cs) - never Address fields or userId.
        _logger.LogInformation("Order processed to dispatch: {Order}", order);

        var key = order.Id.ToString();
        var value = JsonSerializer.Serialize(order, OrderJsonOptions.Default);
        PublishRaw(Topic, key, value);
    }

    public void PublishRaw(string topic, string key, string? value)
    {
        var message = new Message<string, string> { Key = key, Value = value! };
        _producer.Produce(topic, message, report =>
        {
            if (report.Error.IsError)
            {
                _logger.LogError(
                    "Failed to publish message with key {Key} to topic {Topic}: {Error}",
                    key,
                    topic,
                    report.Error.Reason);
            }
        });
    }

    public void Dispose() => _producer.Dispose();
}
