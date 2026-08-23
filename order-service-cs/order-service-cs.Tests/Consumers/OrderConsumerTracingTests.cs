using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using OrderService.Api.Consumers;
using Xunit;

namespace OrderService.Api.Tests.Consumers;

/// <summary>
/// Focused unit tests for <see cref="OrderConsumer.TryExtractParentContext"/> -
/// the consumer-side half of W3C trace-context propagation across the Kafka
/// hop (the producer-side half is covered by OrderProducerTests'
/// PublishRaw_WithAmbientActivity_InjectsW3CTraceparentHeader). Together
/// these two are what let the saga render as one linked trace per order in
/// Jaeger instead of a disconnected root span per hop.
/// </summary>
public class OrderConsumerTracingTests
{
    private static Headers HeadersWithTraceparent(string traceparent)
    {
        var headers = new Headers();
        headers.Add("traceparent", Encoding.UTF8.GetBytes(traceparent));
        return headers;
    }

    [Fact]
    public void TryExtractParentContext_ValidTraceparentHeader_ReturnsMatchingActivityContext()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var traceparent = $"00-{traceId}-{spanId}-01";

        var result = OrderConsumer.TryExtractParentContext(HeadersWithTraceparent(traceparent), out var parentContext);

        Assert.True(result);
        Assert.Equal(traceId, parentContext.TraceId);
        Assert.Equal(spanId, parentContext.SpanId);
    }

    [Fact]
    public void TryExtractParentContext_NullHeaders_ReturnsFalse()
    {
        var result = OrderConsumer.TryExtractParentContext(null, out var parentContext);

        Assert.False(result);
        Assert.Equal(default, parentContext);
    }

    [Fact]
    public void TryExtractParentContext_NoTraceparentHeader_ReturnsFalse()
    {
        var headers = new Headers();
        headers.Add("some-other-header", Encoding.UTF8.GetBytes("value"));

        var result = OrderConsumer.TryExtractParentContext(headers, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryExtractParentContext_MalformedTraceparent_ReturnsFalse()
    {
        var result = OrderConsumer.TryExtractParentContext(HeadersWithTraceparent("not-a-valid-traceparent"), out _);

        Assert.False(result);
    }
}
