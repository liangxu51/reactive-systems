# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This is a tutorial project for Baeldung demonstrating reactive systems in Java. It implements a full-stack e-commerce order processing pipeline using event-driven microservices. **Do not add unrelated code to this module** — it exists solely to illustrate the reactive systems concepts described in the companion article.

## Services

| Service | Port | Description |
|---|---|---|
| `api-gateway` | 8080 | nginx gateway — the only API entry point; owns the `/api/*` routing table, injects the HTTP Basic credential upstream |
| `order-service` | 8080 | REST API; persists orders and orchestrates the workflow |
| `inventory-service` | 8081 | Reserves or reverts product stock |
| `shipping-service` | 8082 | Creates shipment records (only accepts orders between 10:00–18:00); Kafka-only, no REST surface |
| `frontend` | 80 (cluster) / 4200 (dev) | React UI showing reactive vs. blocking order streaming; proxies `/api/` to the gateway |

**Deployment is Kubernetes-only** — the Helm chart in `k8s/helm/reactive-systems` is the single deployment artifact. There is no docker-compose path; it was removed once the Testcontainers launchers and the Skaffold loop replaced it.

In the cluster every backend Service is ClusterIP and only `frontend` is NodePort, so all API traffic arrives through the frontend's `/api/` proxy into the gateway. Backend swagger-ui/actuator are not routed by the gateway (default-deny) — reach them with `kubectl port-forward`. When running a service locally via its Testcontainers launcher, its port is local as usual.

## Build & Run Commands

### Java services (Maven multi-module, Java 21, Spring Boot 3)

```bash
# Build all three services from the root
mvn clean package -pl order-service,inventory-service,shipping-service

# Build a single service
mvn clean package -pl order-service

# Run a single service with Mongo + Kafka supplied by Testcontainers.
# Nothing to install or start first; containers are reaped on exit.
mvn spring-boot:test-run -pl order-service      # port 8080
mvn spring-boot:test-run -pl inventory-service  # port 8081
mvn spring-boot:test-run -pl order-service-vt   # port 8083
```

The launchers (`TestAsyncApplication`, `TestVirtualThreadOrderApplication`,
under `src/test/java`) activate a `local` profile pinning the Basic
credential to `dev`/`dev`, so local calls are scriptable instead of needing
the random password Spring logs each boot. `mvn spring-boot:run` still works
but expects MongoDB and Kafka to already be running somewhere.

### Frontend (React + Vite, plain JavaScript)

Standard npm workflow in `frontend/` — see `frontend/package.json` scripts.

### Run everything on a local cluster (Skaffold)

```bash
skaffold dev                       # rebuild + redeploy on save, streaming logs
skaffold dev -p app-only           # skip the monitoring stack for a faster loop
skaffold dev -p order-service-java # Java order-service instead of the C# one
skaffold run                       # one-shot build and deploy
skaffold delete                    # tear down
```

This deploys the same Helm chart CI deploys, so it exercises the real
topology — gateway routing, credential injection, service-to-service calls,
the Kafka saga. Use it before opening a PR; use the Testcontainers launchers
above while iterating on a single service.

The order-service variant is a chart value, not a build-time choice:
`values.yaml` enables `orderServiceCs` and disables both Java variants. All
three publish the same `order-service` Service name, so the gateway needs no
change when switching — unlike the removed compose setup, which had to be
told which container to target.

Skaffold builds the Java services through `k8s/skaffold/build-java-image.sh`,
which runs `mvn package` first because their Dockerfiles are not multi-stage
(they `COPY target/*.jar`, exactly as CI does it).

## Infrastructure Dependencies

All three Java services require:
- **MongoDB** — same database, separate collections
- **Kafka** — single topic named `orders`

The Testcontainers launchers supply both automatically for local runs, and
the Helm chart deploys both in-cluster. `mvn spring-boot:run` and a plain
`mvn test` of the integration tests are the only paths that expect them to
already exist (`localhost:27017` / `localhost:9092`).

## Architecture: Event-Driven Order Workflow

All inter-service communication flows through a single Kafka topic (`orders`). Each service listens with its own consumer group and reacts to specific `OrderStatus` values:

```
Frontend → POST /api/orders
    → order-service saves order, publishes INITIATION_SUCCESS
        → order-service consumer sees INITIATION_SUCCESS → publishes RESERVE_INVENTORY
        → inventory-service consumer sees RESERVE_INVENTORY → reserves stock → publishes INVENTORY_SUCCESS or INVENTORY_FAILURE
            → order-service consumer sees INVENTORY_SUCCESS → publishes PREPARE_SHIPPING
            → shipping-service consumer sees PREPARE_SHIPPING → creates shipment → publishes SHIPPING_SUCCESS or SHIPPING_FAILURE
                → order-service consumer sees SHIPPING_FAILURE → publishes REVERT_INVENTORY
                → inventory-service consumer sees REVERT_INVENTORY → restores stock → publishes INVENTORY_REVERT_SUCCESS
```

The `OrderStatus` enum in each service (e.g., `order-service/.../constants/OrderStatus.java`) is the shared contract — all three copies must stay in sync.

## Key Design Patterns

- **Reactive stack**: Spring WebFlux + Project Reactor (`Mono`/`Flux`) + Spring Data MongoDB Reactive across all three services.
- **Kafka consumers** (`async/consumer/OrderConsumer`) use `@KafkaListener` and call `.subscribe()` explicitly since the reactive chain is not automatically subscribed inside a Kafka listener.
- **Saga / compensating transactions**: if shipping fails, `order-service` publishes `REVERT_INVENTORY` so `inventory-service` can roll back stock.
- **Domain duplication**: `Order`, `LineItem`, `Address`, `OrderStatus`, and `ObjectIdSerializer` are intentionally duplicated across all three services to keep them deployable independently.

## Frontend: Reactive vs. Blocking Demo

The React app (`frontend/src/`) has two fetching strategies side-by-side, both defined in `api/ordersApi.js` and `hooks/useOrderStream.js`:
- `useOrderStream` — opens a native `EventSource` (SSE) to stream orders reactively from `GET /api/orders` as a `Flux<Order>` with `text/event-stream`, with cleanup on unmount/deactivation via the `useEffect` return function.
- `fetchOrders` (in `ordersApi.js`) — a plain `fetch()` GET that returns the full list at once, triggered from a button click rather than on mount.

The `OrderController` exposes the same `GET /api/orders` endpoint; Spring WebFlux automatically handles SSE when the client sends `Accept: text/event-stream`.
