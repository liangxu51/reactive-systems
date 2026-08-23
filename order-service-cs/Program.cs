// Minimal bootstrap for Task 1 (project scaffold) - full composition
// (MongoDB client/database registration, Kafka producer/consumer, actuator
// equivalents at /actuator/health and /actuator/prometheus, port 8080,
// HTTP Basic auth) lands in Task 4. The two additions below are Task 3's:
// they are needed for OrderController to actually work when the app runs,
// not part of that broader composition root.
using OrderService.Api.Filters;
using OrderService.Api.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers(options => options.Filters.Add<GlobalExceptionFilter>())
    // Mirrors OrderJsonOptions.Default (see Serialization/OrderJsonOptions.cs)
    // so [FromBody] Order model binding on POST - and the built-in JSON
    // output formatter used by ControllerBase.Ok(...) - agree with the wire
    // format OrderController's GET action serializes manually: ObjectId as a
    // plain string, OrderStatus as its enum name, camelCase property names.
    // Without this, binding a POST body containing a LineItem.ProductId
    // (ObjectId) would fail outright - System.Text.Json has no built-in
    // support for MongoDB.Bson.ObjectId.
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = OrderJsonOptions.Default.PropertyNamingPolicy;
        foreach (var converter in OrderJsonOptions.Default.Converters)
        {
            options.JsonSerializerOptions.Converters.Add(converter);
        }
    });

var app = builder.Build();

app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory-based integration tests in later tasks.
public partial class Program
{
}
