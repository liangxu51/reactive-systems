using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using OrderService.Api.Consumers;
using OrderService.Api.Constants;
using OrderService.Api.Domain;
using OrderService.Api.Producers;
using OrderService.Api.Repositories;
using OrderService.Api.Serialization;
using Xunit;
using System.Text.Json;

namespace OrderService.Api.Tests.Consumers;

/// <summary>
/// Covers the fix for the confirmed final-review finding that a genuine
/// shutdown cancellation (stoppingToken firing while a message is
/// in-flight) was previously treated as a retryable failure and, after
/// exhausting retries, dead-lettered a perfectly healthy message purely
/// because the process happened to be shutting down. See
/// OrderConsumer.ProcessWithRetry's XML doc for the full story.
/// </summary>
public class OrderConsumerRetryTests
{
    private readonly Mock<IOrderRepository> _orderRepository = new();
    private readonly Mock<IProcessedEventRepository> _processedEventRepository = new();
    private readonly Mock<IOrderProducer> _orderProducer = new();

    private OrderConsumer CreateConsumer()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        return new OrderConsumer(
            _orderRepository.Object,
            _processedEventRepository.Object,
            _orderProducer.Object,
            loggerFactory.Object);
    }

    private static ConsumeResult<string, string> NewResult(Order order)
    {
        var json = JsonSerializer.Serialize(order, OrderJsonOptions.Default);
        return new ConsumeResult<string, string>
        {
            Message = new Message<string, string>
            {
                Key = order.Id.ToString(),
                Value = json,
                Headers = new Headers(),
            },
        };
    }

    [Fact]
    public void ProcessWithRetry_CancellationDuringShutdown_PropagatesAndDoesNotDeadLetter()
    {
        var order = new Order
        {
            Id = ObjectId.GenerateNewId(),
            UserId = "user-1",
            OrderStatus = OrderStatus.INITIATION_SUCCESS,
        };
        var result = NewResult(order);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Simulates the Mongo driver honoring the (already-cancelled) token
        // mid-HandleAsync - the real-world trigger for this bug.
        _processedEventRepository
            .Setup(r => r.InsertAsync(It.IsAny<ProcessedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var consumer = CreateConsumer();

        Assert.Throws<OperationCanceledException>(() => consumer.ProcessWithRetry(result, cts.Token));

        // Must not have retried (each retry re-enters HandleAsync) or
        // forwarded the message to the dead-letter topic.
        _processedEventRepository.Verify(
            r => r.InsertAsync(It.IsAny<ProcessedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _orderProducer.Verify(
            p => p.PublishRaw(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public void ProcessWithRetry_NonCancellationFailure_StillRetriesThenDeadLetters()
    {
        var order = new Order
        {
            Id = ObjectId.GenerateNewId(),
            UserId = "user-1",
            OrderStatus = OrderStatus.INITIATION_SUCCESS,
        };
        var result = NewResult(order);

        _processedEventRepository
            .Setup(r => r.InsertAsync(It.IsAny<ProcessedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var consumer = CreateConsumer();

        // A no-op token (not cancelled) - the pre-existing retry/DLT
        // behavior for a genuine, non-shutdown failure must be unchanged.
        consumer.ProcessWithRetry(result, CancellationToken.None);

        _processedEventRepository.Verify(
            r => r.InsertAsync(It.IsAny<ProcessedEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        _orderProducer.Verify(
            p => p.PublishRaw("orders.DLT", order.Id.ToString(), result.Message.Value),
            Times.Once);
    }
}
