using MongoDB.Driver;
using Testcontainers.Kafka;
using Testcontainers.MongoDb;

namespace OrderService.DevHost;

// Dev launcher: runs the real API with MongoDB and Kafka supplied by
// Testcontainers, so the inner loop needs no separately installed
// infrastructure and no environment for the repo to drift out of sync with.
//
//   dotnet run --project order-service-cs/OrderService.DevHost
//
// This is the .NET counterpart of the Java services' `mvn
// spring-boot:test-run`. .NET has no equivalent built-in command, hence a
// small project of its own rather than a flag - keeping dev-only container
// wiring out of the API's composition root entirely.
//
// Containers are disposed on exit (Ctrl-C included).
internal static class DevHost
{
    // Same images the Helm chart deploys, so a developer's Mongo and Kafka
    // behave like the cluster's.
    private const string MongoImage = "mongo:4.4";
    private const string KafkaImage = "confluentinc/cp-kafka:7.4.0";

    // Matches the database the Helm chart points every service at.
    private const string DatabaseName = "reactive-systems";

    // The API binds 8080 itself (Program.cs), matching the deployed
    // container and order-service (Java)'s launcher. Override with
    // ASPNETCORE_URLS if something else already holds that port - a
    // kubectl/k9s port-forward is the usual culprit.
    private const string DefaultUrl = "http://0.0.0.0:8080";

    private static async Task<int> Main(string[] args)
    {
        // A single-node replica set, not a standalone mongod: the driver's
        // retryable writes - on by default, and used on the order write path -
        // are a replica-set-only feature, so a standalone container would fail
        // the first POST rather than at startup.
        await using var mongo = new MongoDbBuilder()
            .WithImage(MongoImage)
            .WithReplicaSet()
            .Build();

        await using var kafka = new KafkaBuilder()
            .WithImage(KafkaImage)
            .Build();

        Console.WriteLine($"[devhost] starting {MongoImage} and {KafkaImage} ...");
        await Task.WhenAll(mongo.StartAsync(), kafka.StartAsync());

        // ASP.NET Core maps Section__Key environment variables onto the
        // Section:Key configuration this app reads (Program.cs), which is the
        // same mechanism the Helm chart uses - so the app is configured here
        // exactly the way it is in the cluster, with no dev-only code path.
        // The container's own connection string carries no database path, but
        // Program.cs derives the database from it (MongoUrl.DatabaseName, used
        // for GetDatabase) and throws ArgumentNullException without one. Set it
        // through MongoUrlBuilder rather than string surgery so the credentials
        // and replicaSet options the container generated survive intact.
        var mongoUrl = new MongoUrlBuilder(mongo.GetConnectionString())
        {
            DatabaseName = DatabaseName,
            // Without this the driver authenticates against DatabaseName,
            // where the container's root user does not exist, and startup
            // fails with "Command saslStart failed: Authentication failed".
            // The container creates its user in admin, which is also what the
            // Helm chart's connection string says (authSource=admin).
            AuthenticationSource = "admin",
        }.ToMongoUrl();

        Environment.SetEnvironmentVariable("Mongo__ConnectionString", mongoUrl.ToString());
        Environment.SetEnvironmentVariable("Kafka__BootstrapServers", kafka.GetBootstrapAddress());

        // Without a fixed credential the API would require whatever
        // appsettings.json happens to hold; pinning it makes the loop
        // scriptable. Not a secret: it only ever guards a local process.
        Environment.SetEnvironmentVariable("ApiAuth__Username", "dev");
        Environment.SetEnvironmentVariable("ApiAuth__Password", "dev");

        // No Jaeger locally. Without this the OTLP exporter retries a missing
        // collector every few seconds and buries the app's own logs.
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");


        Console.WriteLine($"[devhost] mongo    -> {mongoUrl}");
        Console.WriteLine($"[devhost] kafka    -> {kafka.GetBootstrapAddress()}");
        var url = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? DefaultUrl;
        Console.WriteLine($"[devhost] api      -> {url} (basic auth dev/dev)");

        // The API uses top-level statements, so its entry point is the
        // compiler-synthesised `<Main>$` - not callable by name, but reachable
        // as the assembly's EntryPoint. `public partial class Program` at the
        // bottom of Program.cs is what gives us a handle on that assembly.
        var entryPoint = typeof(Program).Assembly.EntryPoint
            ?? throw new InvalidOperationException("OrderService.Api has no entry point");

        var result = entryPoint.Invoke(null, [args]);
        if (result is Task task)
        {
            await task;
        }

        return 0;
    }
}
