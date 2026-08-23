using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;

namespace OrderService.Api.Serialization;

/// <summary>
/// Mirrors com.baeldung.serdeser.ObjectIdSerializer / ObjectIdValueSerializer in
/// order-service (Java): an <see cref="ObjectId"/> is written as a plain JSON
/// string (its standard lowercase 24-char hex form, matching ObjectId.ToString()),
/// never as a nested BSON-style object.
/// </summary>
public sealed class ObjectIdJsonConverter : JsonConverter<ObjectId>
{
    public override ObjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return string.IsNullOrEmpty(value) ? ObjectId.Empty : ObjectId.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, ObjectId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
