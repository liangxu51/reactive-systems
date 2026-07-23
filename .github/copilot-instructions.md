# Copilot Instructions for Reactive Systems

This file provides guidance to GitHub Copilot sessions when working in this repository.

## Project Overview

This is a Baeldung tutorial on reactive systems in Java implementing a full-stack event-driven e-commerce order processing pipeline. **Do not add unrelated code** — this module exists solely to demonstrate reactive systems concepts.

**Key constraint**: This is a multi-service architecture with duplicated domain objects (`Order`, `LineItem`, `Address`, `OrderStatus`, `ObjectIdSerializer`) across three services for independent deployability. See "Domain Duplication" below.

## Services at a Glance

| Service | Port | Key Responsibility |
|---------|------|-------------------|
| `order-service` | 8080 | REST entry point; orchestrates workflow via Kafka |
| `inventory-service` | 8081 | Stock management (reserve/revert) |
| `shipping-service` | 8082 | Shipment creation (10:00–18:00 only) |
| `frontend` | 4200 (dev) / 80 (Docker) | React UI: reactive (SSE) vs. blocking fetch demo |

## Build Commands

**Root-level (Maven multi-module, Java 21, Spring Boot 3):**
```bash
# Build all Java services
mvn clean package -pl order-service,inventory-service,shipping-service

# Build one service
mvn clean package -pl order-service
mvn clean package -pl inventory-service
mvn clean package -pl shipping-service

# Build and run order-service locally (MongoDB + Kafka must be running)
mvn spring-boot:run -pl order-service -Dspring-boot.run.arguments=--spring.kafka.bootstrap-servers=localhost:29092

# Build with hot-reload enabled (auto-restart on file changes)
mvn compile -pl order-service
```

**Frontend (React + Vite):**
```bash
cd frontend
npm install
npm run dev      # Dev server at http://localhost:4200
npm run build    # Production build
npm run preview  # Serve built assets locally
```

**All services (Docker Compose):**
```bash
docker-compose up --build
# Starts: Zookeeper, Kafka (29092), MongoDB (27017), all three Java services, nginx frontend
```

## Testing

**Java services use Maven Surefire and Testcontainers.** Docker must be running for integration tests.

```bash
# Run all tests in a service
mvn test -pl order-service

# Run a single test class
mvn test -pl order-service -Dtest=OrderServiceUnitTest

# Run a specific test method
mvn test -pl order-service -Dtest=OrderServiceUnitTest#testMethodName

# Run tests without Testcontainers (unit tests only; some integration tests will be skipped)
mvn test -pl order-service -DskipITs

# Commonly used test classes across services:
# - OrderServiceUnitTest / OrderServiceIntegrationTest (order-service)
# - ProductServiceUnitTest (inventory-service)
# - ShippingServiceUnitTest (shipping-service)
# - OrderProducerSerializationUnitTest (tests JSON serialization for Kafka)
```

**Frontend:** No automated tests; validate manually via `npm run dev` and browser inspection.

## Code Quality & Linting

**Java:**
- Maven enforcer plugin configured in parent `pom.xml`; run `mvn clean package` to validate
- No separate linter; rely on IDE inspections and code review
- Lombok annotations used extensively; ensure IDE supports it

**Frontend:**
- No linter configured; use IDE (VS Code) built-in inspection
- Plain JavaScript (not TypeScript); Bootstrap 4 for styling

## Key Architecture: Kafka Event Workflow

All inter-service communication flows through a **single Kafka topic** (`orders`). Each service:
- Publishes `Order` objects (JSON) to the topic
- Subscribes via `@KafkaListener` (async consumer groups)
- Reacts to specific `OrderStatus` values

**Order flow:**
```
Frontend → POST /api/orders
  → order-service saves & publishes INITIATION_SUCCESS
    → order-service consumer: INITIATION_SUCCESS → publishes RESERVE_INVENTORY
      → inventory-service consumer: RESERVE_INVENTORY → reserves stock → publishes INVENTORY_SUCCESS/FAILURE
        → order-service consumer: INVENTORY_SUCCESS → publishes PREPARE_SHIPPING
          → shipping-service consumer: PREPARE_SHIPPING → creates shipment → publishes SHIPPING_SUCCESS/FAILURE
            → order-service consumer: SHIPPING_FAILURE → publishes REVERT_INVENTORY
              → inventory-service consumer: REVERT_INVENTORY → rolls back stock → publishes INVENTORY_REVERT_SUCCESS
```

**Transactional guarantee:** Failures trigger compensating transactions (e.g., `REVERT_INVENTORY`). This is a saga pattern with manual recovery logic in each service's consumer.

## Reactive Stack Details

All three services use:
- **Spring WebFlux** — non-blocking HTTP (Netty)
- **Project Reactor** — `Mono<T>` (0–1 item) and `Flux<T>` (0–many items)
- **Spring Data MongoDB Reactive** — non-blocking database access

**Critical pattern in Kafka consumers:**
```java
// Inside @KafkaListener, reactive chains are not auto-subscribed
// Explicitly call .subscribe() to invoke the reactive pipeline
kafkaTemplate.executeInTransaction(ops -> {
    return repository.save(order)
        .doOnNext(saved -> publishEvent(saved))
        .subscribe();  // Must call subscribe()!
});
```

## Infrastructure Dependencies

All three Java services require:
- **MongoDB** at `mongodb://localhost:27017/reactive-systems` (same database, separate collections)
- **Kafka** at `localhost:9092` (or `localhost:29092` when connecting from host to Docker container)

Start before running services locally:
```bash
docker-compose up mongodb mongo-init-replicaset zookeeper kafka
```

**Note on Kafka bootstrap servers:** 
- Inside Docker: `kafka:9092` (container-to-container)
- From host machine: `localhost:29092` (published port)
- Configured in `application.properties`; override with `-Dspring-boot.run.arguments=--spring.kafka.bootstrap-servers=localhost:29092`

## Important: Domain Duplication & OrderStatus Sync

The `Order`, `LineItem`, `Address`, `OrderStatus`, and `ObjectIdSerializer` classes are **intentionally duplicated** across all three services:
- Location: `<service>/src/main/java/com/baeldung/{domain,constants,serdeser}/`
- Reason: Independent service deployability — no shared libraries

**Critical rule:** If you modify `OrderStatus` in one service, **update it in all three**:
```bash
# Find all OrderStatus enums
grep -r "enum OrderStatus" . --include="*.java"
```

Same applies to `Order`, `LineItem`, `Address`, and `ObjectIdSerializer`. These are the "shared contracts" between services — divergence will cause deserialization failures.

## API Documentation & Manual Testing

**Swagger UI (auto-generated from controllers):**
- `http://localhost:8080/swagger-ui.html` (order-service)
- `http://localhost:8081/swagger-ui.html` (inventory-service)
- `http://localhost:8082/swagger-ui.html` (shipping-service)

**Manual API testing:**
- Use `order-service/requests.http` with VS Code REST Client or IntelliJ HTTP Client
- Or use `curl`, `httpie`, or Postman

**Frontend reactive demo:**
- `GET /api/orders` with `Accept: text/event-stream` streams orders as SSE (Flux)
- `GET /api/orders` without Accept header returns the full list as JSON

## Common Development Workflows

### Adding a field to Order
1. Update `domain/Order.java` in **all three services**
2. Update `OrderStatus.java` in **all three services** if needed
3. Run `mvn clean package -pl order-service,inventory-service,shipping-service`
4. Test end-to-end with Docker Compose

### Debugging event flow
1. Check MongoDB collections:
   ```bash
   docker exec -it mongo-db mongosh
   use reactive-systems
   db.orders.find()
   ```
2. Check Kafka topic (use `kafka-console-consumer.sh` inside container or `kcat` if installed)
3. Review service logs: each service logs consumed/published events

### Testing a service in isolation
1. Start infrastructure: `docker-compose up mongodb mongo-init-replicaset zookeeper kafka`
2. Run service: `mvn spring-boot:run -pl order-service -Dspring-boot.run.arguments=--spring.kafka.bootstrap-servers=localhost:29092`
3. Run tests: `mvn test -pl order-service`
4. Access Swagger UI to test endpoints manually

### Frontend development
1. `cd frontend && npm install && npm run dev`
2. Dev server at `http://localhost:4200`
3. Changes auto-reload; ensure backend services running on ports 8080–8082

## Verification Checklist Before Committing

- [ ] Run `mvn clean package -pl order-service,inventory-service,shipping-service` to ensure all services build
- [ ] If modified `Order`, `LineItem`, `Address`, `OrderStatus`, or `ObjectIdSerializer`, verify changes applied to **all three services**
- [ ] Run `mvn test -pl order-service` (or affected service) to verify unit/integration tests pass
- [ ] Test end-to-end with `docker-compose up --build` if changing event flow or domain model
- [ ] Check Swagger UI manually to ensure new endpoints are documented
- [ ] For frontend changes, verify `npm run build` succeeds and preview works

## Dev Tools & Environment

- **IDE**: VS Code, IntelliJ, or Eclipse; Lombok support required for Java services
- **Spring Boot DevTools**: Auto-restart on file changes (no manual Maven recompile needed in most IDEs)
- **Git**: Feature branches welcome; follow conventional commits when possible
- **Docker**: Required for local development (Testcontainers, docker-compose)

## Copilot Cloud Agent Setup

The `.github/workflows/copilot-setup-steps.yml` file pre-configures Copilot's development environment with:
- Java 21 and Maven (with dependency caching)
- Node.js 20 and npm (with dependency caching)
- Verification of compilability for Java services and frontend

This allows Copilot cloud agent to build, test, and validate changes faster.

## Recommended MCP Servers

For enhanced Copilot sessions, consider configuring these MCP servers in your Copilot settings:
- **Playwright MCP** — End-to-end testing and browser automation for frontend validation
- **Spring Boot/Java Documentation** — Reference Spring Framework and Spring Boot APIs
- **Kafka Documentation** — Reference Apache Kafka producer/consumer patterns
- **MongoDB Documentation** — Reference MongoDB query and aggregation syntax

These can be configured at the organization or personal Copilot settings level.

## Dataplane V2 & Service Mesh Guidance

When deploying to GKE, prefer cluster-level encryption (Dataplane V2) for in-cluster pod-to-pod traffic:
- Dataplane V2 provides transparent eBPF-based encryption for all pod-to-pod traffic, including app→MongoDB
- Use Istio/Anthos when traffic policies, observability, or advanced routing/telemetry are required
- If MongoDB or Kafka are external managed services, keep app-level TLS to protect those connections

For local **minikube** development:
- Optionally enable Istio for encrypted (mTLS) pod-to-pod communication and to test service-mesh patterns
- See `k8s/ISTIO_MINIKUBE.md` for step-by-step Istio setup on minikube
- See `k8s/GKE_DEPLOYMENT.md` for production GKE deployment with Dataplane V2
