using System.Runtime.CompilerServices;
using MongoDB.Bson.Serialization.Conventions;

namespace OrderService.Api.Repositories;

/// <summary>
/// Registers a camelCase element-name convention for BSON (de)serialization
/// so Order/LineItem/Address/ProcessedEvent documents written to MongoDB use
/// the same field casing (userId, lineItems, shippingAddress, ...) as the
/// Kafka/HTTP JSON wire format (see Serialization.OrderJsonOptions), rather
/// than the driver's default of the raw C# PascalCase member name. This runs
/// once, automatically, as soon as this assembly loads - registration can't
/// be left to Program.cs composition (deferred to Task 4) because any
/// IMongoCollection&lt;T&gt; created by the repositories below needs the
/// convention in place before its first (de)serialization.
/// </summary>
internal static class MongoConventions
{
    [ModuleInitializer]
    public static void Register()
    {
        var pack = new ConventionPack { new CamelCaseElementNameConvention() };
        ConventionRegistry.Register("camelCase", pack, _ => true);
    }
}
