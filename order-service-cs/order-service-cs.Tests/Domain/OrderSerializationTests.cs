using System.Text.Json;
using MongoDB.Bson;
using OrderService.Api.Constants;
using OrderService.Api.Domain;
using OrderService.Api.Serialization;
using Xunit;

namespace OrderService.Api.Tests.Domain;

public class OrderSerializationTests
{
    [Fact]
    public void Serialize_FullyPopulatedOrder_MatchesJavaJacksonWireShape()
    {
        var order = new Order
        {
            Id = ObjectId.Parse("507f1f77bcf86cd799439011"),
            UserId = "user-42",
            LineItems = new List<LineItem>
            {
                new()
                {
                    ProductId = ObjectId.Parse("507f191e810c19729de860ea"),
                    Quantity = 3,
                },
            },
            Total = 1999L,
            PaymentMode = "CARD",
            ShippingAddress = new Address
            {
                Name = "Jane Doe",
                House = "221B",
                Street = "Baker Street",
                City = "London",
                Zip = "NW16XE",
            },
            ShippingDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            OrderStatus = OrderStatus.INITIATION_SUCCESS,
            ResponseMessage = "order accepted",
        };

        var json = JsonSerializer.Serialize(order, OrderJsonOptions.Default);

        const string expected =
            "{"
            + "\"id\":\"507f1f77bcf86cd799439011\","
            + "\"userId\":\"user-42\","
            + "\"lineItems\":[{\"productId\":\"507f191e810c19729de860ea\",\"quantity\":3}],"
            + "\"total\":1999,"
            + "\"paymentMode\":\"CARD\","
            + "\"shippingAddress\":{\"name\":\"Jane Doe\",\"house\":\"221B\",\"street\":\"Baker Street\",\"city\":\"London\",\"zip\":\"NW16XE\"},"
            + "\"shippingDate\":1704067200000,"
            + "\"orderStatus\":\"INITIATION_SUCCESS\","
            + "\"responseMessage\":\"order accepted\""
            + "}";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void Serialize_OrderStatus_UsesExactEnumMemberName()
    {
        var json = JsonSerializer.Serialize(OrderStatus.SHIPPING_FAILURE, OrderJsonOptions.Default);

        Assert.Equal("\"SHIPPING_FAILURE\"", json);
    }

    [Fact]
    public void Serialize_OrderWithUnsetStatus_WritesNull_NotSuccess()
    {
        // Matches Java's nullable orderStatus field: an Order that hasn't
        // had its status assigned yet (e.g. right after a POST, before
        // OrderService.createOrder sets INITIATION_SUCCESS) must serialize
        // orderStatus as JSON null, not silently default to
        // OrderStatus.SUCCESS the way a non-nullable C# enum would.
        var order = new Order { Id = ObjectId.Parse("507f1f77bcf86cd799439011") };

        var json = JsonSerializer.Serialize(order, OrderJsonOptions.Default);

        using var document = JsonDocument.Parse(json);
        var orderStatusProperty = document.RootElement.GetProperty("orderStatus");
        Assert.Equal(JsonValueKind.Null, orderStatusProperty.ValueKind);
    }

    [Fact]
    public void RoundTrip_NullOrderStatus_DeserializesBackToNull()
    {
        const string json = "{\"id\":\"507f1f77bcf86cd799439011\",\"orderStatus\":null}";

        var order = JsonSerializer.Deserialize<Order>(json, OrderJsonOptions.Default);

        Assert.NotNull(order);
        Assert.Null(order!.OrderStatus);
    }

    [Fact]
    public void RoundTrip_SetOrderStatus_PreservesValue()
    {
        var order = new Order
        {
            Id = ObjectId.Parse("507f1f77bcf86cd799439011"),
            OrderStatus = OrderStatus.INVENTORY_SUCCESS,
        };

        var json = JsonSerializer.Serialize(order, OrderJsonOptions.Default);
        var roundTripped = JsonSerializer.Deserialize<Order>(json, OrderJsonOptions.Default);

        Assert.Equal(OrderStatus.INVENTORY_SUCCESS, roundTripped!.OrderStatus);
    }
}
