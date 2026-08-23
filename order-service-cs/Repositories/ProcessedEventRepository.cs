using Microsoft.Extensions.Logging;
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

    public ProcessedEventRepository(IMongoDatabase database, ILogger<ProcessedEventRepository> logger)
    {
        _collection = database.GetCollection<ProcessedEvent>("order_processed_event");

        // TTL index on ProcessedAt (604800s = 7 days), matching Java's
        // @Indexed(expireAfterSeconds = 604800) +
        // spring.data.mongodb.auto-index-creation=true. Created here so it
        // actually runs at startup (this repository is registered as a
        // singleton and gets resolved while the host is starting, before it
        // serves any traffic - see Program.cs) rather than merely being
        // documented as happening somewhere.
        //
        // Confirmed live against a real cluster whose order_processed_event
        // collection was already populated by a Java order-service/
        // order-service-vt deployment: an index on the same key
        // ("processedAt", ascending) already existed there under the name
        // Mongo auto-generated for Java's own createIndex call
        // ("processedAt_1"), but the .NET driver's own auto-generated name
        // for the equivalent CreateIndexModel below did not match it byte
        // for byte, and the server rejects createIndexes outright
        // ("Index with name: processedAt_1 already exists with a different
        // name") rather than treating same-key-different-name as a no-op.
        // The original comment here ("identical options is a safe no-op")
        // only holds when the name matches too - it does not on a cluster
        // that ran a Java variant first, which is exactly the scenario this
        // service must tolerate since all three order-service variants
        // share this collection. Catch that specific conflict and proceed:
        // an equivalent TTL index already existing under a different name
        // still provides the same expiry guarantee this call exists for.
        try
        {
            _collection.Indexes.CreateOne(new CreateIndexModel<ProcessedEvent>(
                Builders<ProcessedEvent>.IndexKeys.Ascending(e => e.ProcessedAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromSeconds(604800) }));
        }
        catch (MongoCommandException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(ex,
                "TTL index on order_processed_event.processedAt already exists (likely created by another order-service variant) - continuing without creating a duplicate.");
        }
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
