namespace OrderService.Api.Constants;

/// <summary>
/// Mirrors com.baeldung.constants.OrderStatus in order-service (Java) field-for-field.
/// Member names are intentionally ALL_CAPS_WITH_UNDERSCORES rather than the
/// idiomatic C# PascalCase: they are serialized verbatim (via
/// System.Text.Json.Serialization.JsonStringEnumConverter, with no naming
/// policy applied) onto the shared Kafka "orders" topic, which inventory-service
/// and shipping-service - both still Java, both still reading the exact enum
/// name string Jackson would have produced - must be able to parse unchanged.
/// </summary>
public enum OrderStatus
{
    SUCCESS,
    FAILURE,
    INITIATION_SUCCESS,
    RESERVE_INVENTORY,
    REVERT_INVENTORY,
    INVENTORY_SUCCESS,
    INVENTORY_FAILURE,
    INVENTORY_REVERT_SUCCESS,
    INVENTORY_REVERT_FAILURE,
    PREPARE_SHIPPING,
    SHIPPING_SUCCESS,
    SHIPPING_FAILURE,
}
