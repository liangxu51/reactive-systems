using System.Diagnostics;

namespace OrderService.Api.Observability;

/// <summary>
/// The single <see cref="ActivitySource"/> this service's hand-instrumented
/// spans are created from - the Kafka producer send
/// (<see cref="Producers.OrderProducer"/>) and consumer receive/process
/// (<see cref="Consumers.OrderConsumer"/>), which ASP.NET Core's own
/// instrumentation (<c>AddAspNetCoreInstrumentation()</c>, covering the
/// inbound HTTP spans) doesn't reach. Mirrors order-service (Java)'s
/// <c>spring.kafka.template.observation-enabled=true</c> /
/// <c>spring.kafka.listener.observation-enabled=true</c>, which give the
/// Kafka producer/listener their own spans per send/receive so the saga's
/// hops show up individually in Jaeger rather than collapsing into just the
/// originating HTTP span.
///
/// Must be registered with the TracerProviderBuilder via
/// <c>.AddSource(Telemetry.ServiceName)</c> in Program.cs - an
/// <see cref="ActivitySource"/> with no registered listener produces activities
/// that are immediately discarded (<see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
/// returns null), so omitting that registration would silently produce no
/// spans at all rather than an error.
/// </summary>
public static class Telemetry
{
    /// <summary>
    /// Also used as the OpenTelemetry Resource's <c>service.name</c>
    /// attribute and Serilog's <c>service.name</c> log field - see the task
    /// report's Decisions for why this exact value (<c>order-service-cs</c>)
    /// was chosen over <c>order-service</c>.
    /// </summary>
    public const string ServiceName = "order-service-cs";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
}
