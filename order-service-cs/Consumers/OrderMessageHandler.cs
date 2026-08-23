using Microsoft.Extensions.Logging;
using OrderService.Api.Constants;
using OrderService.Api.Domain;
using OrderService.Api.Producers;
using OrderService.Api.Repositories;

namespace OrderService.Api.Consumers;

/// <summary>
/// The per-message saga-processing logic for one deserialized <see cref="Order"/>
/// read off the "orders" topic, factored out of <see cref="OrderConsumer"/> so
/// it can be unit tested against mocked repositories/producer without a real
/// Kafka broker or IConsumer wiring. Mirrors the body of
/// com.baeldung.async.consumer.OrderConsumer#consume in order-service (Java).
/// </summary>
public class OrderMessageHandler
{
    // Mirrors order-service (Java)'s OrderConsumer.NEXT_STATUS exactly.
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus> NextStatus =
        new Dictionary<OrderStatus, OrderStatus>
        {
            [OrderStatus.INITIATION_SUCCESS] = OrderStatus.RESERVE_INVENTORY,
            [OrderStatus.INVENTORY_SUCCESS] = OrderStatus.PREPARE_SHIPPING,
            [OrderStatus.SHIPPING_FAILURE] = OrderStatus.REVERT_INVENTORY,
        };

    // Terminal saga failures with no further compensating transaction - the
    // order stays failed and nothing else in the saga acts on it, so they
    // must be logged at error level or they vanish silently. Mirrors
    // order-service (Java)'s OrderConsumer.UNRECOVERABLE_STATUSES.
    private static readonly IReadOnlySet<OrderStatus> UnrecoverableStatuses =
        new HashSet<OrderStatus> { OrderStatus.INVENTORY_FAILURE, OrderStatus.INVENTORY_REVERT_FAILURE };

    private readonly IOrderRepository _orderRepository;
    private readonly IProcessedEventRepository _processedEventRepository;
    private readonly IOrderProducer _orderProducer;
    private readonly ILogger _logger;

    public OrderMessageHandler(
        IOrderRepository orderRepository,
        IProcessedEventRepository processedEventRepository,
        IOrderProducer orderProducer,
        ILogger logger)
    {
        _orderRepository = orderRepository;
        _processedEventRepository = processedEventRepository;
        _orderProducer = orderProducer;
        _logger = logger;
    }

    /// <summary>
    /// Processes one incoming saga message:
    /// 1. Dedup-insert first (redelivery guard) - a duplicate-key collision
    ///    means this (orderId, status) pair was already processed, so it is
    ///    logged and skipped with no further action.
    /// 2. Otherwise, load the persisted order, apply the incoming
    ///    status/response message, and save.
    /// 3. If the incoming status is unrecoverable, log it at error level
    ///    (terminal, nothing else acts on it).
    /// 4. If the incoming status has a next step, publish it via the
    ///    producer (same order, updated to the next status).
    /// </summary>
    public async Task HandleAsync(Order order, CancellationToken cancellationToken = default)
    {
        var orderId = order.Id.ToString();
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["orderId"] = orderId });

        // OrderStatus is nullable (Java's field is null until the saga
        // assigns it) - honor that rather than force-unwrapping. By the time
        // a message reaches this consumer it is always assigned in practice,
        // but a message that somehow arrives without one has no dedup key or
        // NEXT_STATUS entry to act on, so there is nothing safe to do but
        // log and skip.
        if (order.OrderStatus is not { } status)
        {
            _logger.LogWarning("Order {OrderId} message has no OrderStatus - skipping.", orderId);
            return;
        }

        _logger.LogInformation("Order received to process: {Order}", order);

        // Issue #48 (Java): dedup insert first, keyed on (orderId, status).
        // The Mongo status save below is idempotent-by-value on its own, but
        // a redelivered message's re-publish to NEXT_STATUS is not (a
        // redelivered INVENTORY_SUCCESS would double-fire PREPARE_SHIPPING).
        var dedupId = $"{orderId}:{status}";
        try
        {
            await _processedEventRepository.InsertAsync(new ProcessedEvent(dedupId), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DuplicateProcessedEventException)
        {
            _logger.LogInformation(
                "Duplicate {Status} event for order {OrderId}, already processed - skipping.",
                status,
                orderId);
            return;
        }

        var existing = await _orderRepository.FindByIdAsync(order.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            // Matches order-service (Java): findById resolving empty simply
            // completes the reactive chain with no value, so nothing further
            // ever runs for this message.
            _logger.LogWarning("Order {OrderId} not found while processing {Status} event - skipping.", orderId, status);
            return;
        }

        existing.OrderStatus = status;
        existing.ResponseMessage = order.ResponseMessage;
        var saved = await _orderRepository.SaveAsync(existing, cancellationToken).ConfigureAwait(false);

        if (UnrecoverableStatuses.Contains(status))
        {
            _logger.LogError(
                "Order {OrderId} reached unrecoverable status {Status} with no compensating action: {ResponseMessage}",
                orderId,
                status,
                order.ResponseMessage);
        }

        if (NextStatus.TryGetValue(status, out var next))
        {
            saved.OrderStatus = next;
            _orderProducer.SendMessage(saved);
        }
    }
}
