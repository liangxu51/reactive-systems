namespace OrderService.Api.Domain;

/// <summary>
/// Mirrors com.baeldung.domain.ProcessedEvent in order-service (Java). Its Id
/// IS the dedup key - "{orderId}:{status}" (status being the OrderStatus
/// member name string) - so inserting one first turns a Kafka redelivery of
/// the same (orderId, status) pair into a no-op via MongoDB's unique _id
/// index, instead of double-publishing the next saga step. Stored in the
/// "order_processed_event" collection (see ProcessedEventRepository), not the
/// default-derived "processedEvent" collection, so this service's dedup
/// markers never collide with another service's ProcessedEvent documents in
/// the shared database.
///
/// The TTL index (604800s, matching the Java @Indexed(expireAfterSeconds =
/// 604800)) is created at startup by Task 2 - this type only defines the
/// document shape.
/// </summary>
public class ProcessedEvent
{
    public string Id { get; set; } = string.Empty;

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    public ProcessedEvent()
    {
    }

    public ProcessedEvent(string id)
    {
        Id = id;
    }
}
