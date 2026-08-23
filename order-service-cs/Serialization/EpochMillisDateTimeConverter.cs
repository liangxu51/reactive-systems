using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderService.Api.Serialization;

/// <summary>
/// Mirrors Jackson's default handling of java.util.Date with no JavaTimeModule
/// registered (the case in order-service (Java), which relies on Jackson's
/// default Date serialization): a <see cref="DateTime"/> is written as a bare
/// JSON number of milliseconds since the Unix epoch (UTC), never an ISO-8601
/// string.
/// </summary>
public sealed class EpochMillisDateTimeConverter : JsonConverter<DateTime>
{
    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var millis = reader.GetInt64();
        return Epoch.AddMilliseconds(millis);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value.ToUniversalTime(),
        };

        var millis = (long)(utc - Epoch).TotalMilliseconds;
        writer.WriteNumberValue(millis);
    }
}
