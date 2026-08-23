using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderService.Api.Serialization;

/// <summary>
/// Single source of truth for the Kafka/HTTP wire-format JSON shape shared by
/// order-service-cs. Both the future HTTP layer (Controllers/OrderController)
/// and the future Kafka producer/consumer (Producers/OrderProducer,
/// Consumers/OrderConsumer) must (de)serialize <see cref="Domain.Order"/> and
/// friends through <see cref="Default"/> rather than ad hoc
/// JsonSerializerOptions, so the wire shape can never drift between the two
/// call sites.
///
/// camelCase property naming matches Jackson's default property naming
/// (order-service (Java) has no explicit @JsonNaming/@JsonProperty
/// overrides), and the plain (non-naming-policy) JsonStringEnumConverter
/// preserves OrderStatus member names exactly (e.g. "INITIATION_SUCCESS"),
/// matching Jackson's default enum handling (Enum.name()).
/// </summary>
public static class OrderJsonOptions
{
    public static readonly JsonSerializerOptions Default = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        options.Converters.Add(new ObjectIdJsonConverter());
        options.Converters.Add(new EpochMillisDateTimeConverter());
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
