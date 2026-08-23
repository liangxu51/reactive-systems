using System.Text.Json;
using MongoDB.Bson;
using OrderService.Api.Serialization;
using Xunit;

namespace OrderService.Api.Tests.Serialization;

public class ObjectIdJsonConverterTests
{
    [Fact]
    public void Serialize_WritesPlainLowercaseHexString_NotAnObject()
    {
        var id = ObjectId.Parse("507f1f77bcf86cd799439011");

        var json = JsonSerializer.Serialize(id, OrderJsonOptions.Default);

        Assert.Equal("\"507f1f77bcf86cd799439011\"", json);
    }

    [Fact]
    public void Deserialize_ParsesPlainHexString_BackToSameObjectId()
    {
        var json = "\"507f1f77bcf86cd799439011\"";

        var id = JsonSerializer.Deserialize<ObjectId>(json, OrderJsonOptions.Default);

        Assert.Equal(ObjectId.Parse("507f1f77bcf86cd799439011"), id);
    }

    [Fact]
    public void RoundTrip_PreservesValue()
    {
        var id = ObjectId.GenerateNewId();

        var json = JsonSerializer.Serialize(id, OrderJsonOptions.Default);
        var roundTripped = JsonSerializer.Deserialize<ObjectId>(json, OrderJsonOptions.Default);

        Assert.Equal(id, roundTripped);
    }
}
