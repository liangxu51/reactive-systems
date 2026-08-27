// Task 4 composition root: finalizes the bootstrap Task 1-3 left in place
// (MVC/controllers registration, the global exception filter, JSON options)
// with everything needed to actually run this service end-to-end - HTTP
// Basic auth on every endpoint, OpenTelemetry tracing, Serilog structured
// JSON logging, Prometheus metrics, a health endpoint, real Mongo/Kafka
// wiring, and OrderConsumer registered as a hosted BackgroundService so the
// saga consumer actually runs.
//
// Configuration keys introduced here (see the Task 4 report's Decisions
// list for the full rationale and env-var equivalents Task 5/6 must wire
// up): ApiAuth:Username, ApiAuth:Password, Jaeger:OtlpEndpoint,
// Kafka:BootstrapServers, Mongo:ConnectionString. None of appsettings.json
// / appsettings.Docker.json exist yet (Task 5) - every key below has a
// local-dev default baked into this file so `dotnet run` still starts
// without them, matching CLAUDE.md's documented local defaults
// (mongodb://localhost:27017/reactive-systems, Kafka at localhost:9092).
using Confluent.Kafka;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using MongoDB.Driver;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderService.Api.Auth;
using OrderService.Api.Consumers;
using OrderService.Api.Filters;
using OrderService.Api.Logging;
using OrderService.Api.Observability;
using OrderService.Api.Producers;
using OrderService.Api.Repositories;
using OrderService.Api.Serialization;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Fixed external contract: port 8080 (see Global Constraints) - bound on
// every interface (0.0.0.0), not just localhost, so it's reachable from
// outside the container once Task 5/6 run this under Docker/Kubernetes,
// matching order-service (Java)'s server.port=8080 (Spring Boot also binds
// 0.0.0.0 by default).
//
// Read from configuration first so the default can be overridden the way
// Spring's server.port can be (ASPNETCORE_URLS=http://0.0.0.0:8090, or a Urls
// entry in appsettings). A bare UseUrls literal outranks both ASPNETCORE_URLS
// and ASPNETCORE_HTTP_PORTS, which left no way to move the port for a local
// run when something else already held 8080. The deployed default is
// unchanged.
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://0.0.0.0:8080");

// ---- Logging (Serilog, flat ECS-compatible JSON console output) ----
// Replaces the default logging providers entirely so every log line -
// framework/ASP.NET Core internals included, not just this app's own
// ILogger calls - goes through EcsCompatJsonFormatter. See that class for
// why the field names/shape matter (Promtail's existing json pipeline
// stage) and Order.ToString()'s override (Domain/Order.cs) for why no code
// path here needs to special-case PII redaction itself: nothing in this
// composition root logs an Order's properties individually, only via
// Order.ToString() (already non-PII) through the {Order} message template
// placeholders in OrderController/OrderService/OrderProducer/OrderMessageHandler.
builder.Host.UseSerilog((_, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .WriteTo.Console(new EcsCompatJsonFormatter(Telemetry.ServiceName));
});

// ---- Auth: HTTP Basic on every endpoint, no anonymous-allowed path ----
// Matches order-service (Java)'s SecurityConfig
// (.authorizeExchange(exchanges -> exchanges.anyExchange().authenticated())).
builder.Services
    .AddAuthentication(BasicAuthenticationHandler.SchemeName)
    .AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>(BasicAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

// ---- Tracing: OpenTelemetry .NET SDK, OTLP exporter to Jaeger ----
// Jaeger:OtlpEndpoint is the *base* endpoint only (e.g. http://jaeger:4318,
// no /v1/traces suffix) - this composition root appends the OTLP HTTP
// traces path itself below, so Task 5/6 only ever need to supply the bare
// host:port, matching the Jaeger Service address the Helm chart deploys.
var otlpBaseEndpoint = (builder.Configuration["Jaeger:OtlpEndpoint"] ?? "http://localhost:4318").TrimEnd('/');
builder.Services.AddOpenTelemetry()
    // Feeds the exported spans' service.name resource attribute - without
    // it every service would report as OTel's generic "unknown_service"
    // default in Jaeger, indistinguishable from every other pod (see
    // order-service (Java)'s application.properties comment on
    // spring.application.name for the same problem/fix on that side).
    .ConfigureResource(resource => resource.AddService(Telemetry.ServiceName))
    .WithTracing(tracing => tracing
        // Every request traced, not a sampled fraction - matches Java's
        // management.tracing.sampling.probability=1.0 (fine at this demo's
        // traffic volume; would need lowering under real production load).
        .SetSampler(new AlwaysOnSampler())
        // Kafka producer send / consumer receive spans, hand-instrumented
        // in Producers/OrderProducer.cs and Consumers/OrderConsumer.cs -
        // see Observability/Telemetry.cs for why AddSource is required for
        // those Activities to actually produce exported spans.
        .AddSource(Telemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(otlp =>
        {
            otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
            otlp.Endpoint = new Uri($"{otlpBaseEndpoint}/v1/traces");
        }));

// ---- MongoDB ----
// The MongoDB C# driver's MongoClient/IMongoCollection are documented as
// thread-safe and meant to be shared as a single instance per process
// (unlike, say, an EF Core DbContext) - registering them (and the thin
// repository wrappers over them) as singletons matches that guidance and
// keeps every downstream service (IOrderService, OrderConsumer) safely
// resolvable as a singleton too, avoiding a captured-scoped-service-in-
// singleton DI validation error for OrderConsumer's BackgroundService below.
var mongoConnectionString = builder.Configuration["Mongo:ConnectionString"] ?? "mongodb://localhost:27017/reactive-systems";
var mongoUrl = MongoUrl.Create(mongoConnectionString);
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoUrl));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(mongoUrl.DatabaseName));
builder.Services.AddSingleton<IOrderRepository, OrderRepository>();
builder.Services.AddSingleton<IProcessedEventRepository, ProcessedEventRepository>();

// ---- Kafka producer + application services ----
// OrderProducer.CreateProducer sets Acks = Acks.All (see Producers/OrderProducer.cs)
// - do not construct the IProducer<string, string> any other way here.
var kafkaBootstrapServers = builder.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
builder.Services.AddSingleton<IProducer<string, string>>(_ => OrderProducer.CreateProducer(kafkaBootstrapServers));
builder.Services.AddSingleton<IOrderProducer, OrderProducer>();
builder.Services.AddSingleton<OrderService.Api.Services.IOrderService, OrderService.Api.Services.OrderService>();

// ---- OrderConsumer: the saga consumer, registered as a hosted BackgroundService ----
// Built via a factory delegate rather than AddHostedService<OrderConsumer>()
// so the Kafka bootstrap-servers value above (from configuration) is passed
// explicitly - OrderConsumer's constructor parameter is a plain string with
// a "localhost:9092" default value, which the DI container's default-
// parameter fallback would otherwise silently use instead of our configured
// value (the container only resolves constructor parameters it has a
// registered service for; a bare string is never one, regardless of what
// other string-typed configuration exists).
builder.Services.AddHostedService(sp => new OrderConsumer(
    sp.GetRequiredService<IOrderRepository>(),
    sp.GetRequiredService<IProcessedEventRepository>(),
    sp.GetRequiredService<IOrderProducer>(),
    sp.GetRequiredService<ILoggerFactory>(),
    kafkaBootstrapServers));

builder.Services
    .AddControllers(options =>
    {
        options.Filters.Add<GlobalExceptionFilter>();
        // Global auth requirement for every MVC controller action - the
        // AddAuthorization() FallbackPolicy above already covers minimal-API
        // endpoints that don't explicitly call .RequireAuthorization() (see
        // /actuator/health and /actuator/prometheus below, which use
        // RequireAuthorization() explicitly for clarity anyway), but MVC
        // controllers resolve authorization through filters rather than the
        // endpoint-level fallback policy, so this AuthorizeFilter is what
        // actually enforces it for OrderController.
        options.Filters.Add(new AuthorizeFilter());
    })
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
    })
    // Discover this assembly's controllers explicitly. MVC otherwise scans
    // the *entry* assembly, which is this one when the app is launched
    // normally but not when another host starts it in-process (see
    // OrderService.DevHost) - there every route 404s while authentication
    // still works, because auth is registered by hand and controllers are
    // not. No effect on the deployed app, where the two assemblies coincide.
    .AddApplicationPart(typeof(Program).Assembly);

var app = builder.Build();

// HTTP request metrics (request count/duration by method/route/status) -
// must run ahead of authentication/authorization so a rejected (401) request
// is still counted. Fixed external contract: this app's own metrics are
// exposed at /actuator/prometheus (not the package's /metrics default) so
// the existing Helm ServiceMonitor (Task 6, written for the Java variants)
// keeps scraping the same path.
app.UseHttpMetrics();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Fixed external contract: exact paths /actuator/prometheus and
// /actuator/health - the CI smoke test curls the latter, and Task 6's Helm
// ServiceMonitor scrapes the former; both must require auth (Global
// Constraints - no anonymous-allowed path, including these), which
// RequireAuthorization() enforces explicitly here since neither is an MVC
// controller action covered by the AuthorizeFilter above.
app.MapMetrics("/actuator/prometheus").RequireAuthorization();
app.MapGet("/actuator/health", () => Results.Ok(new { status = "UP" })).RequireAuthorization();

app.Run();

// Exposed for WebApplicationFactory-based integration tests in later tasks.
public partial class Program
{
}
