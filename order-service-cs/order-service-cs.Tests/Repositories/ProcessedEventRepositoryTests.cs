using Moq;
using MongoDB.Driver;
using OrderService.Api.Domain;
using OrderService.Api.Repositories;
using Xunit;

namespace OrderService.Api.Tests.Repositories;

/// <summary>
/// Covers the fix for the confirmed final-review finding that no TTL index
/// was ever created on the "order_processed_event" collection, so dedup
/// markers never expired. IMongoDatabase/IMongoCollection/IMongoIndexManager
/// are all interfaces in the MongoDB C# driver, so the constructor's index
/// creation can be verified against a mock without a real MongoDB instance.
/// </summary>
public class ProcessedEventRepositoryTests
{
    [Fact]
    public void Constructor_CreatesTtlIndexOnProcessedAt_With604800SecondExpiry()
    {
        var indexManager = new Mock<IMongoIndexManager<ProcessedEvent>>();
        var collection = new Mock<IMongoCollection<ProcessedEvent>>();
        collection.SetupGet(c => c.Indexes).Returns(indexManager.Object);
        var database = new Mock<IMongoDatabase>();
        database
            .Setup(d => d.GetCollection<ProcessedEvent>("order_processed_event", null))
            .Returns(collection.Object);

        _ = new ProcessedEventRepository(database.Object);

        indexManager.Verify(
            m => m.CreateOne(
                It.Is<CreateIndexModel<ProcessedEvent>>(model =>
                    model.Options != null &&
                    model.Options.ExpireAfter == TimeSpan.FromSeconds(604800)),
                null,
                default),
            Times.Once);
    }
}
