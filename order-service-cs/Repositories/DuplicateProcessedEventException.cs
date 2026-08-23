namespace OrderService.Api.Repositories;

/// <summary>
/// Thrown by <see cref="ProcessedEventRepository.InsertAsync"/> when the
/// dedup key ("{orderId}:{status}", see ProcessedEvent) has already been
/// recorded - i.e. a duplicate MongoDB _id key collision - so callers (Task
/// 2's OrderConsumer) can treat a Kafka redelivery as a no-op instead of
/// re-running saga side effects, matching order-service (Java)'s handling of
/// Spring Data's DuplicateKeyException.
/// </summary>
public sealed class DuplicateProcessedEventException : Exception
{
    public string DedupKey { get; }

    public DuplicateProcessedEventException(string dedupKey, Exception innerException)
        : base($"ProcessedEvent with id '{dedupKey}' already exists.", innerException)
    {
        DedupKey = dedupKey;
    }
}
