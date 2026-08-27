## Reactive Systems in Java

This module contains services for article about reactive systems in Java. Please note that these services comprise parts of a full stack application to demonstrate the capabilities of a reactive system. Unless there is an article which extends on this concept, this is probably not a suitable module to add other code.

> ⚠️ **Demo only — not for production.** The backends require HTTP Basic auth and the nginx `api-gateway` injects that credential at the edge, but the edge itself is open — anyone who can reach the gateway (or frontend) can use the API, and `GET /api/orders` returns all order data (including customer PII). Add real edge authentication and scope data access before any real deployment.

The gateway is the only API entry point: it owns the `/api/*` routing table and injects the credential the backends require. In the cluster it is a ClusterIP service reached through the frontend, which proxies `/api/` to it. See `docs/superpowers/specs/2026-08-22-api-gateway-design.md`.

Deployment is Kubernetes-only, via the Helm chart in `k8s/helm/reactive-systems`. There is no docker-compose path.

## Local development

### One service, fast loop

Run the service under active development with MongoDB and Kafka supplied by Testcontainers. Nothing needs installing beyond Docker, and there is no infrastructure to start first:

```bash
mvn spring-boot:test-run -pl order-service      # port 8080  (Java)
mvn spring-boot:test-run -pl inventory-service  # port 8081  (Java)
mvn spring-boot:test-run -pl order-service-vt   # port 8083  (Java, virtual threads)

dotnet run --project order-service-cs/OrderService.DevHost   # port 8080  (C#/.NET)
```

Each launcher (`TestAsyncApplication`, `TestVirtualThreadOrderApplication`, `OrderService.DevHost`) can also be run from an IDE for a debugger on the app while its backing services stay real containers. Containers are torn down when the process exits.

`order-service` and `order-service-cs` are alternative implementations of the same service and both take port 8080, so run one at a time. `order-service-vt` uses 8083 and can run alongside either, to compare the reactive and virtual-thread stacks side by side.

Every launcher pins the HTTP Basic credential to `dev`/`dev`, so calls are scriptable rather than needing the random password the framework would otherwise generate on each boot:

```bash
curl -u dev:dev localhost:8080/api/orders
```

`order-service` has `spring-boot-devtools` on the classpath, so it auto-restarts once a source change is recompiled — most IDEs recompile on save automatically; if running from the CLI, rerun `mvn compile -pl order-service` in another terminal to trigger it.

To exercise the API manually:
- Use `order-service/requests.http` with the VS Code REST Client extension or IntelliJ's built-in HTTP Client.
- Or browse `http://localhost:8080/swagger-ui.html` (springdoc-openapi, auto-generated from the controllers).

#### Verifying a launcher is working

The same four checks apply to any of them (adjust the port):

```bash
# 1. Authenticated request succeeds, unauthenticated is rejected.
#    Both matter: 200 alone can come from something else already on the port.
curl -s -o /dev/null -w '%{http_code}\n' -u dev:dev localhost:8080/api/orders   # 200
curl -s -o /dev/null -w '%{http_code}\n' localhost:8080/api/orders              # 401

# 2. Confirm it is your process answering, not a stray kubectl/k9s port-forward
ss -ltnp | grep :8080

# 3. Mongo and Kafka really came up (ports are random per run)
docker ps --format '{{.Image}}\t{{.Ports}}' | grep -E 'mongo|kafka'

# 4. Write path end to end - an order should persist and read back
curl -s -u dev:dev -X POST localhost:8080/api/orders \
  -H 'Content-Type: application/json' \
  -d '{"userId":"local","lineItems":[{"quantity":1}],"total":10,
       "paymentMode":"Cash on Delivery",
       "shippingAddress":{"name":"D","house":"1","street":"M","city":"B","zip":"02101"},
       "shippingDate":1790000000000}'
curl -s -u dev:dev -H 'Accept: application/json' localhost:8080/api/orders
```

`shippingDate` is epoch milliseconds, not an ISO string — both implementations reject the latter.

To confirm the Kafka round-trip rather than just the produce, look at the consumer group inside the broker container:

```bash
KC=$(docker ps --filter ancestor=confluentinc/cp-kafka:7.4.0 --format '{{.Names}}' | head -1)
docker exec "$KC" kafka-consumer-groups --bootstrap-server localhost:9093 --describe --group orders
```

Note `localhost:9093`, the broker's internal listener — `9092` is published on a random host port and is not reachable by that name from inside the container. A healthy result shows the group with `LAG 0` and an end offset above zero.

Two things to expect on a single-service run. The saga cannot complete, because the other services are not running — an order stays at `INITIATION_SUCCESS` (or reaches `RESERVE_INVENTORY` on the topic and stops). And the consumer uses `auto.offset.reset=latest`, so the very first order after a cold start can be produced before group assignment finishes and is then skipped; a second order always shows the round-trip.

#### C#-specific notes

`OrderService.DevHost` is a separate project rather than a flag, because .NET has no equivalent of `spring-boot:test-run`. It starts the containers, exports the same `Section__Key` variables the Helm chart sets, and then runs the real API in-process.

The port is overridable, which the Java services get from `server.port`:

```bash
ASPNETCORE_URLS=http://0.0.0.0:8090 dotnet run --project order-service-cs/OrderService.DevHost
```

Useful when a `kubectl port-forward` or k9s session already holds 8080 — the symptom otherwise is `Failed to bind to address http://0.0.0.0:8080: address already in use` after the containers have already started.

### The whole system, real topology

For anything spanning services — gateway routing, credential injection, the Kafka saga — deploy the actual chart to a local cluster with Skaffold:

```bash
skaffold dev                      # rebuild and redeploy on save
skaffold dev -p app-only          # skip the monitoring stack for a faster loop
skaffold dev -p order-service-java # use the Java order-service instead of the C# one
skaffold delete                   # tear down
```

This deploys the same chart CI deploys, so it exercises the real topology rather than an approximation. It is the slower outer loop — reach for the Testcontainers launchers above while iterating on a single service.

To run the test suite (`mvn test`), Docker must be running — integration tests spin up a real MongoDB via Testcontainers.
