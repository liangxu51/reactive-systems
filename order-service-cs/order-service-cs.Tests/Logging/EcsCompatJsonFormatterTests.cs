using System.Diagnostics;
using System.Text.Json;
using OrderService.Api.Logging;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace OrderService.Api.Tests.Logging;

/// <summary>
/// Verifies EcsCompatJsonFormatter emits exactly the flat, top-level field
/// names Promtail's existing json pipeline stage extracts
/// (k8s/helm/reactive-systems/values.yaml's
/// loki-stack.promtail.config.snippets.pipelineStages): log.level,
/// service.name, traceId, spanId, orderId. Getting these wrong produces no
/// error - Promtail just silently stops labeling this service's logs in
/// Grafana/Loki - so this is worth pinning down directly rather than
/// trusting the wiring by inspection alone.
/// </summary>
public class EcsCompatJsonFormatterTests
{
    private static LogEvent CreateLogEvent(LogEventLevel level, string message, IEnumerable<LogEventProperty>? properties = null, Exception? exception = null)
    {
        var template = new MessageTemplateParser().Parse(message);
        return new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            exception,
            template,
            properties ?? Array.Empty<LogEventProperty>());
    }

    [Theory]
    [InlineData(LogEventLevel.Verbose, "TRACE")]
    [InlineData(LogEventLevel.Debug, "DEBUG")]
    [InlineData(LogEventLevel.Information, "INFO")]
    [InlineData(LogEventLevel.Warning, "WARN")]
    [InlineData(LogEventLevel.Error, "ERROR")]
    [InlineData(LogEventLevel.Fatal, "FATAL")]
    public void Format_MapsLevelToUppercaseEcsValue_NotSerilogsOwnCasing(LogEventLevel level, string expectedEcsLevel)
    {
        // The Grafana "ERROR log rate by service" dashboard panel
        // (k8s/helm/reactive-systems/templates/grafana-dashboards.yaml)
        // queries Loki with an exact-match level="ERROR" selector - Serilog's
        // own LogEventLevel.ToString() casing/names ("Error", not "ERROR")
        // would silently never match that query. Pinning down every level's
        // exact mapping here, not just Error, since the same silent-mismatch
        // risk applies to any future panel querying on another level.
        var formatter = new EcsCompatJsonFormatter("order-service-cs");
        var logEvent = CreateLogEvent(level, "level mapping check");

        using var writer = new StringWriter();
        formatter.Format(logEvent, writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(expectedEcsLevel, doc.RootElement.GetProperty("log.level").GetString());
    }

    [Fact]
    public void Format_EmitsFlatTopLevelDottedKeys_ForLevelAndServiceName()
    {
        var formatter = new EcsCompatJsonFormatter("order-service-cs");
        var logEvent = CreateLogEvent(LogEventLevel.Information, "hello world");

        using var writer = new StringWriter();
        formatter.Format(logEvent, writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("log.level", out var logLevel));
        Assert.Equal("INFO", logLevel.GetString());

        Assert.True(root.TryGetProperty("service.name", out var serviceName));
        Assert.Equal("order-service-cs", serviceName.GetString());

        Assert.Equal("hello world", root.GetProperty("message").GetString());

        // Must NOT be nested under a "log" or "service" object - Promtail's
        // extraction expects these as literal top-level keys containing a
        // dot character, not a nested {"log":{"level":...}} shape.
        Assert.False(root.TryGetProperty("log", out _));
        Assert.False(root.TryGetProperty("service", out _));
    }

    [Fact]
    public void Format_NoActivity_OmitsTraceAndSpanId()
    {
        Assert.Null(Activity.Current);
        var formatter = new EcsCompatJsonFormatter("order-service-cs");
        var logEvent = CreateLogEvent(LogEventLevel.Information, "no active span");

        using var writer = new StringWriter();
        formatter.Format(logEvent, writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.TryGetProperty("traceId", out _));
        Assert.False(doc.RootElement.TryGetProperty("spanId", out _));
    }

    [Fact]
    public void Format_WithActivity_IncludesTraceIdAndSpanId()
    {
        using var activity = new Activity("test-activity").Start();

        var formatter = new EcsCompatJsonFormatter("order-service-cs");
        var logEvent = CreateLogEvent(LogEventLevel.Information, "within a span");

        using var writer = new StringWriter();
        formatter.Format(logEvent, writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        Assert.Equal(activity.TraceId.ToString(), root.GetProperty("traceId").GetString());
        Assert.Equal(activity.SpanId.ToString(), root.GetProperty("spanId").GetString());

        activity.Stop();
    }

    [Fact]
    public void Format_WithOrderIdProperty_IncludesOrderIdAsTopLevelField()
    {
        var formatter = new EcsCompatJsonFormatter("order-service-cs");
        var properties = new[] { new LogEventProperty("orderId", new ScalarValue("507f1f77bcf86cd799439011")) };
        var logEvent = CreateLogEvent(LogEventLevel.Information, "order event", properties);

        using var writer = new StringWriter();
        formatter.Format(logEvent, writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("507f1f77bcf86cd799439011", doc.RootElement.GetProperty("orderId").GetString());
    }

    [Fact]
    public void Format_WithoutOrderIdProperty_OmitsOrderIdField()
    {
        var formatter = new EcsCompatJsonFormatter("order-service-cs");
        var logEvent = CreateLogEvent(LogEventLevel.Information, "no order context");

        using var writer = new StringWriter();
        formatter.Format(logEvent, writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.TryGetProperty("orderId", out _));
    }

    [Fact]
    public void Format_DoesNotLeakOrderPiiFields()
    {
        // Guards against a caller accidentally passing an Order's raw
        // properties (e.g. {UserId}, {ShippingAddress}) into a log
        // template instead of the whole Order via its redacted ToString() -
        // this formatter has no way to strip such fields after the fact, so
        // the guarantee lives entirely in never emitting properties it
        // wasn't told to (only the fixed set below, plus orderId).
        var formatter = new EcsCompatJsonFormatter("order-service-cs");
        var properties = new[]
        {
            new LogEventProperty("orderId", new ScalarValue("abc")),
            new LogEventProperty("UserId", new ScalarValue("user-42")),
            new LogEventProperty("ShippingAddress", new ScalarValue("123 Main St")),
        };
        var logEvent = CreateLogEvent(LogEventLevel.Information, "order event", properties);

        using var writer = new StringWriter();
        formatter.Format(logEvent, writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var fieldNames = new List<string>();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            fieldNames.Add(property.Name);
        }

        Assert.Contains("orderId", fieldNames);
        Assert.DoesNotContain("UserId", fieldNames);
        Assert.DoesNotContain("ShippingAddress", fieldNames);
    }
}
