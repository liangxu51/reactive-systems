namespace OrderService.Api.Domain;

/// <summary>Mirrors com.baeldung.domain.Address in order-service (Java) field-for-field.</summary>
public class Address
{
    public string? Name { get; set; }

    public string? House { get; set; }

    public string? Street { get; set; }

    public string? City { get; set; }

    public string? Zip { get; set; }

    // SEC-004 (defense in depth): redact PII at the source so any type that
    // embeds an Address and logs it via a default-generated ToString() - not
    // just Order, which has its own explicit override - never leaks
    // name/house/street/city/zip. Matches order-service (Java)'s override.
    public override string ToString() => "Address[redacted]";
}
