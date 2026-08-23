using System.Text.Json;
using OrderService.Api.Serialization;
using Xunit;

namespace OrderService.Api.Tests.Serialization;

public class EpochMillisDateTimeConverterTests
{
    [Fact]
    public void Serialize_WritesBareEpochMillisNumber_NotAString()
    {
        var date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var json = JsonSerializer.Serialize(date, OrderJsonOptions.Default);

        Assert.Equal("1704067200000", json);
    }

    [Fact]
    public void Deserialize_ParsesEpochMillisNumber_BackToUtcDateTime()
    {
        var json = "1704067200000";

        var date = JsonSerializer.Deserialize<DateTime>(json, OrderJsonOptions.Default);

        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), date);
        Assert.Equal(DateTimeKind.Utc, date.Kind);
    }

    [Fact]
    public void RoundTrip_PreservesMillisecondPrecision()
    {
        var date = new DateTime(2026, 8, 22, 13, 45, 30, 123, DateTimeKind.Utc);

        var json = JsonSerializer.Serialize(date, OrderJsonOptions.Default);
        var roundTripped = JsonSerializer.Deserialize<DateTime>(json, OrderJsonOptions.Default);

        Assert.Equal(date, roundTripped);
    }
}
