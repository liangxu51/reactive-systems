using System.Diagnostics;
using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace OrderService.Api.Logging;

/// <summary>
/// Serilog <see cref="ITextFormatter"/> producing one flat JSON object per
/// log line, shaped to match order-service (Java)'s Spring Boot ECS
/// (Elastic Common Schema) structured console format
/// (<c>logging.structured.format.console=ecs</c>) closely enough for
/// Promtail's existing <c>json</c> pipeline stage
/// (k8s/helm/reactive-systems/values.yaml's
/// <c>loki-stack.promtail.config.snippets.pipelineStages</c>) to keep
/// working unmodified against this service too.
///
/// Promtail extracts these exact **top-level** field names -
/// <c>log.level</c>, <c>service.name</c>, <c>traceId</c>, <c>spanId</c>,
/// <c>orderId</c> - and promotes level/service to Loki labels. Getting the
/// field names or nesting wrong means logs silently stop being labeled in
/// Grafana/Loki: no error, just missing labels. Note "log.level" and
/// "service.name" are single flat JSON keys containing a literal dot
/// character (ECS's ".-namespaced" flat-key convention), not a nested
/// <c>{"log":{"level":...}}</c> object - hand-writing the object with
/// System.Text.Json here (rather than trusting an off-the-shelf formatter's
/// default property names/nesting) is what guarantees that shape.
///
/// traceId/spanId are read from the ambient <see cref="Activity.Current"/>
/// (populated by OpenTelemetry's ASP.NET Core/Activity instrumentation, see
/// Program.cs) when present. orderId is read from the current log event's
/// properties, populated via the <c>ILogger</c> scope
/// <c>OrderMessageHandler.HandleAsync</c> already pushes
/// (<c>_logger.BeginScope(new Dictionary&lt;string, object&gt; { ["orderId"] = orderId })</c>)
/// - Serilog's Microsoft.Extensions.Logging bridge (via Serilog.AspNetCore)
/// automatically turns an <c>IEnumerable&lt;KeyValuePair&lt;string,object&gt;&gt;</c>
/// scope state into log event properties, no extra wiring needed.
/// </summary>
public sealed class EcsCompatJsonFormatter : ITextFormatter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _serviceName;

    public EcsCompatJsonFormatter(string serviceName)
    {
        _serviceName = serviceName;
    }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        var fields = new Dictionary<string, object?>
        {
            ["@timestamp"] = logEvent.Timestamp.UtcDateTime.ToString("O"),
            ["log.level"] = logEvent.Level.ToString(),
            ["service.name"] = _serviceName,
            ["message"] = logEvent.RenderMessage(),
        };

        var activity = Activity.Current;
        if (activity is not null)
        {
            fields["traceId"] = activity.TraceId.ToString();
            fields["spanId"] = activity.SpanId.ToString();
        }

        if (logEvent.Properties.TryGetValue("orderId", out var orderIdValue))
        {
            fields["orderId"] = Unwrap(orderIdValue);
        }

        if (logEvent.Exception is not null)
        {
            fields["error.message"] = logEvent.Exception.Message;
            fields["error.stack_trace"] = logEvent.Exception.ToString();
        }

        output.Write(JsonSerializer.Serialize(fields, SerializerOptions));
        output.Write(Environment.NewLine);
    }

    /// <summary>
    /// Unwraps a Serilog <see cref="LogEventPropertyValue"/> to a plain CLR
    /// value for JSON serialization - a bare <see cref="ScalarValue.ToString"/>
    /// would return the value pre-quoted (e.g. <c>"\"abc\""</c> for a
    /// string), which would double-quote it again once re-serialized here.
    /// </summary>
    private static object? Unwrap(LogEventPropertyValue value) => value switch
    {
        ScalarValue scalar => scalar.Value,
        _ => value.ToString(),
    };
}
