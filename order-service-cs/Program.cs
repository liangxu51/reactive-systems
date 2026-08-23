// Minimal bootstrap for Task 1 (project scaffold) - full composition
// (MongoDB client/database registration, Kafka producer/consumer, actuator
// equivalents at /actuator/health and /actuator/prometheus, port 8080) lands
// in Task 4.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory-based integration tests in later tasks.
public partial class Program
{
}
