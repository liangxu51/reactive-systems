using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using OrderService.Api.Constants;

namespace OrderService.Api.Domain;

/// <summary>Mirrors com.baeldung.domain.Order in order-service (Java) field-for-field.</summary>
public class Order
{
    // MongoDB.Driver's default ID convention maps a member named "Id"
    // (case-insensitively) to the document's "_id" field, matching Spring
    // Data's @Id -> _id mapping in the Java reference.
    public ObjectId Id { get; set; }

    public string? UserId { get; set; }

    public List<LineItem>? LineItems { get; set; }

    public long? Total { get; set; }

    public string? PaymentMode { get; set; }

    public Address? ShippingAddress { get; set; }

    public DateTime? ShippingDate { get; set; }

    // Stored as its member name string in MongoDB (matching Spring Data's
    // default enum-as-name-string mapping), not the driver's default
    // integer representation.
    [BsonRepresentation(BsonType.String)]
    public OrderStatus OrderStatus { get; set; }

    public string? ResponseMessage { get; set; }

    // SEC-004: a default-generated ToString() would recurse into
    // ShippingAddress (name/house/street/city/zip) and UserId, writing
    // unredacted customer PII to plaintext logs on every log call that
    // interpolates an Order. Override ToString() to log only non-PII fields,
    // matching order-service (Java)'s override.
    public override string ToString() =>
        $"Order[id={Id}, orderStatus={OrderStatus}, lineItems={LineItems?.Count ?? 0} items]";
}
