using Microsoft.Extensions.Logging;
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
    private static Mock<IMongoDatabase> SetUpDatabase(out Mock<IMongoIndexManager<ProcessedEvent>> indexManager)
    {
        indexManager = new Mock<IMongoIndexManager<ProcessedEvent>>();
        var collection = new Mock<IMongoCollection<ProcessedEvent>>();
        collection.SetupGet(c => c.Indexes).Returns(indexManager.Object);
        var database = new Mock<IMongoDatabase>();
        database
            .Setup(d => d.GetCollection<ProcessedEvent>("order_processed_event", null))
            .Returns(collection.Object);
        return database;
    }

    [Fact]
    public void Constructor_CreatesTtlIndexOnProcessedAt_With604800SecondExpiry()
    {
        var database = SetUpDatabase(out var indexManager);

        _ = new ProcessedEventRepository(database.Object, Mock.Of<ILogger<ProcessedEventRepository>>());

        indexManager.Verify(
            m => m.CreateOne(
                It.Is<CreateIndexModel<ProcessedEvent>>(model =>
                    model.Options != null &&
                    model.Options.ExpireAfter == TimeSpan.FromSeconds(604800)),
                null,
                default),
            Times.Once);
    }

    /// <summary>
    /// Confirmed live against a real cluster whose order_processed_event
    /// collection was already populated by a Java order-service/
    /// order-service-vt deployment: the server rejects createIndexes with
    /// "Index with name: processedAt_1 already exists with a different
    /// name" rather than treating it as a no-op, because the .NET driver's
    /// own auto-generated name for the equivalent index doesn't match
    /// Java's. Startup must tolerate this - an equivalent TTL index already
    /// existing under a different name still provides the same expiry
    /// guarantee.
    /// </summary>
    [Fact]
    public void Constructor_IndexAlreadyExistsUnderDifferentName_LogsWarning_DoesNotThrow()
    {
        var database = SetUpDatabase(out var indexManager);
        indexManager
            .Setup(m => m.CreateOne(It.IsAny<CreateIndexModel<ProcessedEvent>>(), null, default))
            .Throws(new MongoCommandException(
                new MongoDB.Driver.Core.Connections.ConnectionId(new MongoDB.Driver.Core.Servers.ServerId(
                    new MongoDB.Driver.Core.Clusters.ClusterId(), new System.Net.DnsEndPoint("localhost", 27017))),
                "Command createIndexes failed: Index with name: processedAt_1 already exists with a different name.",
                command: null,
                result: null));
        var loggerMock = new Mock<ILogger<ProcessedEventRepository>>();

        var exception = Record.Exception(() => new ProcessedEventRepository(database.Object, loggerMock.Object));

        Assert.Null(exception);
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
