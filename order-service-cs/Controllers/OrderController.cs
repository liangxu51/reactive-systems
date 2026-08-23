using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OrderService.Api.Constants;
using OrderService.Api.Domain;
using OrderService.Api.Serialization;
using OrderService.Api.Services;

namespace OrderService.Api.Controllers;

/// <summary>
/// Mirrors com.baeldung.reactive.controller.OrderController in order-service
/// (Java): POST /api/orders creates an order (mapping a saga FAILURE result
/// to an error response) and GET /api/orders returns every order, either as
/// a server-sent-event stream (for a native browser EventSource) or as a
/// plain JSON array (for a plain fetch()) depending on the request's Accept
/// header. Route and port ("api/orders", 8080 - the latter set by Task 4's
/// composition root) are a fixed external contract: frontend/src/api/ordersApi.js
/// and frontend/src/hooks/useOrderStream.js both depend on this exact shape.
/// </summary>
[ApiController]
[Route("api/orders")]
public class OrderController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrderController> _logger;

    public OrderController(IOrderService orderService, ILogger<OrderController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Order order, CancellationToken cancellationToken)
    {
        // Order.ToString() is overridden to log only id/status/item-count
        // (see Domain/Order.cs) - never Address fields or userId.
        _logger.LogInformation("Create order invoked with: {Order}", order);

        var result = await _orderService.CreateOrderAsync(order, cancellationToken).ConfigureAwait(false);

        if (result.OrderStatus == OrderStatus.FAILURE)
        {
            // Distinct error path for a saga-level failure (matches Java's
            // Mono.error(new RuntimeException("Order processing failed,
            // please try again later. " + o.getResponseMessage())), but
            // returns the message directly rather than letting it fall
            // through to the generic exception filter, which would discard
            // it in favor of "An unexpected error occurred" - see Decisions
            // in the task report.
            _logger.LogWarning("Order processing failed: {Order}", result);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { message = $"Order processing failed, please try again later. {result.ResponseMessage}" });
        }

        return Ok(result);
    }

    [HttpGet]
    public async Task GetAll(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Get all orders invoked.");

        var acceptHeader = Request.Headers.Accept.ToString();
        var wantsEventStream = acceptHeader.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);

        var orders = await _orderService.GetOrdersAsync(cancellationToken).ConfigureAwait(false);

        if (wantsEventStream)
        {
            await WriteEventStreamAsync(orders, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await WriteJsonArrayAsync(orders, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes each order as its own SSE frame, flushing after every write so
    /// a native EventSource (frontend/src/hooks/useOrderStream.js) receives
    /// them one at a time rather than buffered until the response completes.
    /// The stream is finite (matches the Java reference's finite snapshot
    /// Flux): it completes and closes the connection once every order has
    /// been sent, which useOrderStream.js's onerror handler already accounts
    /// for.
    /// </summary>
    private async Task WriteEventStreamAsync(List<Order> orders, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";

        foreach (var order in orders)
        {
            var json = JsonSerializer.Serialize(order, OrderJsonOptions.Default);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken).ConfigureAwait(false);
            await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteJsonArrayAsync(List<Order> orders, CancellationToken cancellationToken)
    {
        Response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(orders, OrderJsonOptions.Default);
        await Response.WriteAsync(json, cancellationToken).ConfigureAwait(false);
    }
}
