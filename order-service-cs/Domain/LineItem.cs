using MongoDB.Bson;

namespace OrderService.Api.Domain;

/// <summary>Mirrors com.baeldung.domain.LineItem in order-service (Java) field-for-field.</summary>
public class LineItem
{
    public ObjectId ProductId { get; set; }

    public int Quantity { get; set; }
}
