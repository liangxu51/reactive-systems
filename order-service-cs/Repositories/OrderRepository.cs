using MongoDB.Bson;
using MongoDB.Driver;
using OrderService.Api.Domain;

namespace OrderService.Api.Repositories;

/// <summary>
/// Seam over <see cref="OrderRepository"/> so Task 2's OrderConsumer message
/// handling can be unit tested against a mocked/fake repository instead of a
/// real MongoDB connection.
/// </summary>
public interface IOrderRepository
{
    Task<List<Order>> FindAllAsync(CancellationToken cancellationToken = default);

    Task<Order?> FindByIdAsync(ObjectId id, CancellationToken cancellationToken = default);

    Task<Order> SaveAsync(Order order, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thin wrapper over IMongoCollection&lt;Order&gt;, backed by the "order"
/// collection (Spring Data's default: the decapitalized class name -
/// matches order-service (Java), which never overrides the collection name
/// for Order).
/// </summary>
public class OrderRepository : IOrderRepository
{
    private readonly IMongoCollection<Order> _collection;

    public OrderRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<Order>("order");
    }

    public async Task<List<Order>> FindAllAsync(CancellationToken cancellationToken = default)
    {
        return await _collection.Find(FilterDefinition<Order>.Empty)
            .ToListAsync(cancellationToken);
    }

    public async Task<Order?> FindByIdAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        return await _collection.Find(o => o.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Upserts by Id, matching Spring Data's save() semantics: generates a
    /// new ObjectId when the order hasn't been assigned one yet
    /// (ObjectId.Empty, the C# struct default - Java's nullable ObjectId
    /// equivalent), otherwise replaces the existing document with that Id.
    /// </summary>
    public async Task<Order> SaveAsync(Order order, CancellationToken cancellationToken = default)
    {
        if (order.Id == ObjectId.Empty)
        {
            order.Id = ObjectId.GenerateNewId();
        }

        await _collection.ReplaceOneAsync(
            o => o.Id == order.Id,
            order,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);

        return order;
    }
}
