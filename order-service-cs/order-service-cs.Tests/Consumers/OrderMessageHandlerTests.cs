using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using OrderService.Api.Consumers;
using OrderService.Api.Constants;
using OrderService.Api.Domain;
using OrderService.Api.Producers;
using OrderService.Api.Repositories;
using Xunit;

namespace OrderService.Api.Tests.Consumers;

public class OrderMessageHandlerTests
{
    private readonly Mock<IOrderRepository> _orderRepository = new();
    private readonly Mock<IProcessedEventRepository> _processedEventRepository = new();
    private readonly Mock<IOrderProducer> _orderProducer = new();
    private readonly Mock<ILogger> _logger = new();

    private OrderMessageHandler CreateHandler() => new(
        _orderRepository.Object,
        _processedEventRepository.Object,
        _orderProducer.Object,
        _logger.Object);

    private static Order NewOrder(ObjectId id, OrderStatus status, string? responseMessage = null) => new()
    {
        Id = id,
        UserId = "user-1",
        OrderStatus = status,
        ResponseMessage = responseMessage,
    };

    [Fact]
    public async Task HandleAsync_FreshInitiationSuccess_InsertsDedupMarker_Saves_AndPublishesReserveInventory()
    {
        var orderId = ObjectId.GenerateNewId();
        var incoming = NewOrder(orderId, OrderStatus.INITIATION_SUCCESS, "order accepted");
        var persisted = NewOrder(orderId, OrderStatus.SUCCESS);

        _processedEventRepository
            .Setup(r => r.InsertAsync(It.IsAny<ProcessedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orderRepository
            .Setup(r => r.FindByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(persisted);
        // SaveAsync returns the same Order instance the caller then mutates
        // in place (status -> next status) before publishing, so capture the
        // status/message it was saved with at call time rather than
        // asserting against that instance after HandleAsync returns.
        OrderStatus? savedStatus = null;
        string? savedResponseMessage = null;
        _orderRepository
            .Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback<Order, CancellationToken>((o, _) =>
            {
                savedStatus = o.OrderStatus;
                savedResponseMessage = o.ResponseMessage;
            })
            .ReturnsAsync((Order o, CancellationToken _) => o);

        await CreateHandler().HandleAsync(incoming);

        _processedEventRepository.Verify(
            r => r.InsertAsync(
                It.Is<ProcessedEvent>(e => e.Id == $"{orderId}:INITIATION_SUCCESS"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _orderRepository.Verify(
            r => r.SaveAsync(It.Is<Order>(o => o.Id == orderId), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(OrderStatus.INITIATION_SUCCESS, savedStatus);
        Assert.Equal("order accepted", savedResponseMessage);

        _orderProducer.Verify(
            p => p.SendMessage(It.Is<Order>(o => o.Id == orderId && o.OrderStatus == OrderStatus.RESERVE_INVENTORY)),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DuplicateEvent_DoesNotSaveOrPublish()
    {
        var orderId = ObjectId.GenerateNewId();
        var incoming = NewOrder(orderId, OrderStatus.INITIATION_SUCCESS);

        _processedEventRepository
            .Setup(r => r.InsertAsync(It.IsAny<ProcessedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateProcessedEventException($"{orderId}:INITIATION_SUCCESS", new Exception("dup")));

        await CreateHandler().HandleAsync(incoming);

        _orderRepository.Verify(
            r => r.FindByIdAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _orderRepository.Verify(
            r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _orderProducer.Verify(p => p.SendMessage(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InventoryFailure_LogsError_AndDoesNotPublish()
    {
        var orderId = ObjectId.GenerateNewId();
        var incoming = NewOrder(orderId, OrderStatus.INVENTORY_FAILURE, "stock unavailable");
        var persisted = NewOrder(orderId, OrderStatus.RESERVE_INVENTORY);

        _processedEventRepository
            .Setup(r => r.InsertAsync(It.IsAny<ProcessedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orderRepository
            .Setup(r => r.FindByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(persisted);
        _orderRepository
            .Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order o, CancellationToken _) => o);

        await CreateHandler().HandleAsync(incoming);

        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // INVENTORY_FAILURE is terminal - no NEXT_STATUS entry.
        _orderProducer.Verify(p => p.SendMessage(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ShippingFailure_PublishesRevertInventory()
    {
        var orderId = ObjectId.GenerateNewId();
        var incoming = NewOrder(orderId, OrderStatus.SHIPPING_FAILURE, "shipping window closed");
        var persisted = NewOrder(orderId, OrderStatus.PREPARE_SHIPPING);

        _processedEventRepository
            .Setup(r => r.InsertAsync(It.IsAny<ProcessedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orderRepository
            .Setup(r => r.FindByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(persisted);
        _orderRepository
            .Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order o, CancellationToken _) => o);

        await CreateHandler().HandleAsync(incoming);

        _orderProducer.Verify(
            p => p.SendMessage(It.Is<Order>(o => o.Id == orderId && o.OrderStatus == OrderStatus.REVERT_INVENTORY)),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_InventorySuccess_PublishesPrepareShipping()
    {
        var orderId = ObjectId.GenerateNewId();
        var incoming = NewOrder(orderId, OrderStatus.INVENTORY_SUCCESS, "inventory reserved");
        var persisted = NewOrder(orderId, OrderStatus.RESERVE_INVENTORY);

        _processedEventRepository
            .Setup(r => r.InsertAsync(It.IsAny<ProcessedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orderRepository
            .Setup(r => r.FindByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(persisted);
        _orderRepository
            .Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order o, CancellationToken _) => o);

        await CreateHandler().HandleAsync(incoming);

        _orderProducer.Verify(
            p => p.SendMessage(It.Is<Order>(o => o.Id == orderId && o.OrderStatus == OrderStatus.PREPARE_SHIPPING)),
            Times.Once);
    }
}
