using Microsoft.Extensions.Logging;
using OrderService.Api.Constants;
using OrderService.Api.Domain;
using OrderService.Api.Producers;
using OrderService.Api.Repositories;

namespace OrderService.Api.Services;

/// <summary>
/// Seam over <see cref="OrderService"/> so Controllers/OrderController can be
/// unit tested against a mocked/fake service instead of real Mongo/Kafka
/// dependencies.
/// </summary>
public interface IOrderService
{
    Task<Order> CreateOrderAsync(Order order, CancellationToken cancellationToken = default);

    Task<List<Order>> GetOrdersAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Mirrors com.baeldung.reactive.service.OrderService in order-service
/// (Java): filters zero-quantity line items, persists the order, publishes
/// INITIATION_SUCCESS to kick off the saga, and persists the status update.
/// Any exception in that sequence is caught and turned into a saved FAILURE
/// order rather than propagating - matching Java's
/// <c>.onErrorResume(err -&gt; Mono.just(order.setOrderStatus(FAILURE)...))</c>,
/// which resumes the reactive chain with a value instead of erroring it.
/// </summary>
public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderProducer _orderProducer;
    private readonly ILogger<OrderService> _logger;

    public OrderService(IOrderRepository orderRepository, IOrderProducer orderProducer, ILogger<OrderService> logger)
    {
        _orderRepository = orderRepository;
        _orderProducer = orderProducer;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Order.ToString() is overridden to log only id/status/item-count
        // (see Domain/Order.cs) - never Address fields or userId.
        _logger.LogInformation("Create order invoked with: {Order}", order);

        try
        {
            // Mutates the same Order instance in place (fluent-setter style
            // in the Java reference mutates and returns `this`), so `order`
            // carries this filtering through the rest of the method -
            // including the catch block below, matching the Java reference,
            // where the "original order" referenced in onErrorResume is the
            // same object identity as the one passed through .map/.save.
            order.LineItems = order.LineItems?.Where(l => l.Quantity > 0).ToList() ?? order.LineItems;

            var saved = await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);

            saved.OrderStatus = OrderStatus.INITIATION_SUCCESS;
            _orderProducer.SendMessage(saved);

            return await _orderRepository.SaveAsync(saved, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            order.OrderStatus = OrderStatus.FAILURE;
            order.ResponseMessage = ex.Message;
            return await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<List<Order>> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Get all orders invoked.");
        return _orderRepository.FindAllAsync(cancellationToken);
    }
}
