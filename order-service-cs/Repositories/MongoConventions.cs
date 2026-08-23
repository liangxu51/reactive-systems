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
///
/// Also registers IgnoreExtraElementsConvention - confirmed live against a
/// real cluster whose order collection was already populated by the Java
/// order-service/order-service-vt (both Spring Data, which by default
/// writes a "_class" discriminator field on every document): without this,
/// GET /api/orders threw FormatException ("Element '_class' does not match
/// any field or property of class Order") on every single request the
/// moment the query's result cursor touched one such document, since the
/// .NET driver rejects unmapped elements by default. Order/LineItem/
/// Address/ProcessedEvent never need to round-trip an unknown element back
/// out, so silently ignoring one on read is safe.
/// </summary>
internal static class MongoConventions
{
    [ModuleInitializer]
    public static void Register()
    {
        var pack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreExtraElementsConvention(true),
        };
        ConventionRegistry.Register("camelCase", pack, _ => true);
    }
}
