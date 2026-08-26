## Reactive Systems in Java

This module contains services for article about reactive systems in Java. Please note that these services comprise parts of a full stack application to demonstrate the capabilities of a reactive system. Unless there is an article which extends on this concept, this is probably not a suitable module to add other code.

> ⚠️ **Demo only — not for production.** The backends require HTTP Basic auth and the nginx `api-gateway` injects that credential at the edge, but the edge itself is open — anyone who can reach the gateway (or frontend) can use the API, and `GET /api/orders` returns all order data (including customer PII). Add real edge authentication and scope data access before any real deployment.

The gateway is the only API entry point: it owns the `/api/*` routing table and injects the credential the backends require. In the cluster it is a ClusterIP service reached through the frontend, which proxies `/api/` to it. See `docs/superpowers/specs/2026-08-22-api-gateway-design.md`.

Deployment is Kubernetes-only, via the Helm chart in `k8s/helm/reactive-systems`. There is no docker-compose path.

## Local development

### One service, fast loop

Run the service under active development with MongoDB and Kafka supplied by Testcontainers. Nothing needs installing beyond Docker, and there is no infrastructure to start first:

```bash
mvn spring-boot:test-run -pl order-service      # port 8080
mvn spring-boot:test-run -pl inventory-service  # port 8081
mvn spring-boot:test-run -pl order-service-vt   # port 8083
```

Each launcher (`TestAsyncApplication`, `TestVirtualThreadOrderApplication`) can also be run from an IDE for a debugger on the app while its backing services stay real containers. They listen on different ports, so `order-service` and `order-service-vt` can run side by side to compare the reactive and virtual-thread stacks. Containers are torn down when the process exits.

The launchers activate a `local` profile that pins the HTTP Basic credential to `dev`/`dev`, so calls are scriptable rather than needing the random password Spring logs on each boot:

```bash
curl -u dev:dev localhost:8080/api/orders
```

`order-service` has `spring-boot-devtools` on the classpath, so it auto-restarts once a source change is recompiled — most IDEs recompile on save automatically; if running from the CLI, rerun `mvn compile -pl order-service` in another terminal to trigger it.

To exercise the API manually:
- Use `order-service/requests.http` with the VS Code REST Client extension or IntelliJ's built-in HTTP Client.
- Or browse `http://localhost:8080/swagger-ui.html` (springdoc-openapi, auto-generated from the controllers).

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
