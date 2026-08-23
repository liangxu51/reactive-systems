using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using OrderService.Api.Constants;
using OrderService.Api.Domain;
using OrderService.Api.Producers;
using OrderService.Api.Repositories;
using Xunit;
using OrderServiceUnderTest = OrderService.Api.Services.OrderService;

namespace OrderService.Api.Tests.Services;

public class OrderServiceTests
{
    private readonly Mock<IOrderRepository> _orderRepository = new();
    private readonly Mock<IOrderProducer> _orderProducer = new();
    private readonly Mock<ILogger<OrderServiceUnderTest>> _logger = new();

    private OrderServiceUnderTest CreateService() =>
        new(_orderRepository.Object, _orderProducer.Object, _logger.Object);

    private static Order NewOrder(params int[] quantities)
    {
        var lineItems = quantities
            .Select(q => new LineItem { ProductId = ObjectId.GenerateNewId(), Quantity = q })
            .ToList();

        return new Order
        {
            UserId = "user-1",
            LineItems = lineItems,
            Total = 100,
            PaymentMode = "Cash on Delivery",
        };
    }

    [Fact]
    public async Task CreateOrderAsync_FiltersZeroAndNegativeQuantityLineItemsBeforeSaving()
    {
        var order = NewOrder(0, 2, -1, 5);

        SetupSaveEchoesInput();

        await CreateService().CreateOrderAsync(order);

        _orderRepository.Verify(
            r => r.SaveAsync(
                It.Is<Order>(o => o.LineItems != null && o.LineItems.All(l => l.Quantity > 0)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        Assert.NotNull(order.LineItems);
        Assert.Equal(2, order.LineItems!.Count);
        Assert.All(order.LineItems, l => Assert.True(l.Quantity > 0));
    }

    [Fact]
    public async Task CreateOrderAsync_HappyPath_SavesTwice_PublishesInitiationSuccess_AndReturnsSavedOrder()
    {
        var order = NewOrder(3);
        SetupSaveEchoesInput();

        var result = await CreateService().CreateOrderAsync(order);

        _orderRepository.Verify(
            r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        _orderProducer.Verify(
            p => p.SendMessage(It.Is<Order>(o => o.OrderStatus == OrderStatus.INITIATION_SUCCESS)),
            Times.Once);

        Assert.Equal(OrderStatus.INITIATION_SUCCESS, result.OrderStatus);
    }

    [Fact]
    public async Task CreateOrderAsync_RepositorySaveThrows_ReturnsSavedFailureOrder_DoesNotThrow()
    {
        var order = NewOrder(1);
        var failure = new InvalidOperationException("mongo is down");

        _orderRepository
            .SetupSequence(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure) // first save (initial persist) fails
            .ReturnsAsync(order); // save in the catch block - same reference, mutated by then

        var result = await CreateService().CreateOrderAsync(order);

        Assert.Equal(OrderStatus.FAILURE, result.OrderStatus);
        Assert.Equal("mongo is down", result.ResponseMessage);

        _orderRepository.Verify(
            r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _orderProducer.Verify(p => p.SendMessage(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrderAsync_ProducerThrows_ReturnsSavedFailureOrder_DoesNotThrow()
    {
        var order = NewOrder(1);
        var failure = new InvalidOperationException("kafka is down");

        SetupSaveEchoesInput();
        _orderProducer.Setup(p => p.SendMessage(It.IsAny<Order>())).Throws(failure);

        var result = await CreateService().CreateOrderAsync(order);

        Assert.Equal(OrderStatus.FAILURE, result.OrderStatus);
        Assert.Equal("kafka is down", result.ResponseMessage);
    }

    [Fact]
    public async Task GetOrdersAsync_DelegatesToRepositoryFindAll()
    {
        var orders = new List<Order> { NewOrder(1), NewOrder(2) };
        _orderRepository.Setup(r => r.FindAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(orders);

        var result = await CreateService().GetOrdersAsync();

        Assert.Same(orders, result);
    }

    private void SetupSaveEchoesInput() =>
        _orderRepository
            .Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order o, CancellationToken _) => o);
}
