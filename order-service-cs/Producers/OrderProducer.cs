using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OrderService.Api.Domain;
using OrderService.Api.Observability;
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
/// topic, same key (order id as lowercase hex), same fire-and-forget send
/// with an error-only completion callback. Durability (acks=all, matching
/// Java's spring.kafka.producer.acks=all) is enforced by
/// <see cref="CreateProducerConfig"/>/<see cref="CreateProducer"/> below -
/// an <see cref="OrderProducer"/> only wraps whatever
/// IProducer&lt;string, string&gt; it is constructed with, so acks=all is a
/// property of how that producer was built, not of this class itself.
/// Callers (Task 4's composition root) must build the real producer through
/// <see cref="CreateProducer"/> to get that guarantee - do not hand-roll a
/// bare <c>new ProducerBuilder&lt;string, string&gt;(new ProducerConfig
/// { BootstrapServers = ... })</c> elsewhere without also setting
/// <c>Acks = Acks.All</c>.
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

    /// <summary>
    /// Builds the <see cref="ProducerConfig"/> for the real Kafka producer,
    /// with <c>Acks = Acks.All</c> - matching order-service (Java)'s
    /// spring.kafka.producer.acks=all (see #40: a produce only completes
    /// once every in-sync replica has the write, not just the partition
    /// leader). Exposed separately from <see cref="CreateProducer"/> so the
    /// config itself (and specifically the acks setting) can be asserted on
    /// directly in tests without opening a real client connection.
    /// </summary>
    public static ProducerConfig CreateProducerConfig(string bootstrapServers) => new()
    {
        BootstrapServers = bootstrapServers,
        Acks = Acks.All,
    };

    /// <summary>
    /// Builds the real Confluent.Kafka IProducer&lt;string, string&gt; this
    /// class should be constructed with, with acks=all applied via
    /// <see cref="CreateProducerConfig"/>. Not wired into Program.cs yet -
    /// that composition happens in Task 4 - but the acks=all guarantee lives
    /// here, in code this task owns, rather than being left as an
    /// undocumented assumption for a later task to (maybe) get right.
    /// </summary>
    public static IProducer<string, string> CreateProducer(string bootstrapServers) =>
        new ProducerBuilder<string, string>(CreateProducerConfig(bootstrapServers)).Build();

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
        // Single choke point for every Kafka send (SendMessage's normal
        // publish and OrderConsumer's DLT forward both funnel through here),
        // so one Activity here covers both - matches order-service (Java)'s
        // spring.kafka.template.observation-enabled=true giving every
        // KafkaTemplate.send(...) its own span.
        using var activity = Telemetry.ActivitySource.StartActivity("kafka.produce", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topic);
        activity?.SetTag("messaging.kafka.message.key", key);

        var message = new Message<string, string> { Key = key, Value = value! };

        // Propagate the current trace context across the Kafka hop as a W3C
        // "traceparent" header - matches order-service (Java)'s
        // spring.kafka.template.observation-enabled=true, which injects
        // traceparent into the record's headers automatically. Without this,
        // OrderConsumer's "kafka.consume" span (see Consumers/OrderConsumer.cs)
        // has no parent to attach to, and each saga hop shows up in Jaeger as
        // its own disconnected root trace instead of one linked trace per
        // order. Activity.Current, not just `activity` above, since
        // StartActivity returns null (no span created) when nothing is
        // listening on Telemetry.ActivitySource - in that case the ambient
        // parent (e.g. the inbound HTTP request's own Activity) should still
        // propagate rather than silently dropping the header.
        var traceparent = Activity.Current?.Id;
        if (traceparent is not null)
        {
            message.Headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes(traceparent) } };
        }

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
