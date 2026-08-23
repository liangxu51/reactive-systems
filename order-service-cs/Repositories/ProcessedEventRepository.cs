using MongoDB.Driver;
using OrderService.Api.Domain;

namespace OrderService.Api.Repositories;

/// <summary>
/// Seam over <see cref="ProcessedEventRepository"/> so Task 2's OrderConsumer
/// message handling can be unit tested against a mocked/fake repository
/// instead of a real MongoDB connection.
/// </summary>
public interface IProcessedEventRepository
{
    Task InsertAsync(ProcessedEvent processedEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thin wrapper over IMongoCollection&lt;ProcessedEvent&gt;, backed by the
/// explicitly-named "order_processed_event" collection (see ProcessedEvent
/// for why this must not use the default-derived collection name).
/// </summary>
public class ProcessedEventRepository : IProcessedEventRepository
{
    private readonly IMongoCollection<ProcessedEvent> _collection;

    public ProcessedEventRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ProcessedEvent>("order_processed_event");

        // TTL index on ProcessedAt (604800s = 7 days), matching Java's
        // @Indexed(expireAfterSeconds = 604800) +
        // spring.data.mongodb.auto-index-creation=true. Created here so it
        // actually runs at startup (this repository is registered as a
        // singleton and gets resolved while the host is starting, before it
        // serves any traffic - see Program.cs) rather than merely being
        // documented as happening somewhere. Creating an index that already
        // exists with identical options is a safe MongoDB no-op, so this can
        // run unconditionally on every startup with no guard needed.
        _collection.Indexes.CreateOne(new CreateIndexModel<ProcessedEvent>(
            Builders<ProcessedEvent>.IndexKeys.Ascending(e => e.ProcessedAt),
            new CreateIndexOptions { ExpireAfter = TimeSpan.FromSeconds(604800) }));
    }

    /// <summary>
    /// Inserts a dedup marker. Throws <see cref="DuplicateProcessedEventException"/>
    /// (rather than letting the driver's MongoWriteException propagate
    /// as-is) on a duplicate-key collision, so callers can distinguish "this
    /// event was already processed" from any other write failure - matching
    /// order-service (Java)'s DuplicateKeyException handling.
    /// </summary>
    public async Task InsertAsync(ProcessedEvent processedEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            await _collection.InsertOneAsync(processedEvent, options: null, cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new DuplicateProcessedEventException(processedEvent.Id, ex);
        }
    }
}
